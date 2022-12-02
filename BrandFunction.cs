using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using BrandFunction.Domain;
using BrandFunction.Domain.Models;
using System.Linq;
using BrandFunction.Helpers;
using Microsoft.Net.Http.Headers;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;

namespace BrandFunction
{
    public class BrandFunction
    {
        private readonly IService _service;
        private readonly IAuthorization _authorization;
        private readonly string[] _permittedExtensions = { ".jpg", ".jpeg", ".png" };
        private readonly long _fileSizeLimit = 104857600;

        public BrandFunction(IService service, IAuthorization authorization)
        {
            _service = service;
            _authorization = authorization;
        }

        [FunctionName("CheckHealth")]
        public async Task<IActionResult> CheckHealth([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req, ILogger log)
        {
            log.LogInformation("CheckHealth function processed a request.");

            string name = req.Query["name"];

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            name ??= data?.name;

            string responseMessage = string.IsNullOrEmpty(name)
                ? "This HTTP triggered function executed successfully. Pass a name in the query string or in the request body for a personalized response."
                : $"Hello, {name}. This HTTP triggered function executed successfully.";

            return new OkObjectResult(responseMessage);
        }

        [FunctionName("PrepareBrand")]
        public async Task<IActionResult> PrepareBrand([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req, ILogger log)
        {
            try
            {
                string token = req.Headers["Authorization"];

                if (token.StartsWith("Bearer"))
                {
                    token = token.Substring(7);
                }

                if (_authorization.ValidateToken(token) && _authorization.HasReadAccess(token))
                {
                    var preparedData = await _service.PrepareBrand();
                    if (preparedData != null)
                    {
                        return new OkObjectResult(JsonConvert.SerializeObject(preparedData));
                    }
                    else
                    {
                        return new NotFoundResult();
                    }
                }
                else
                {
                    return new UnauthorizedResult();
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex.Message);
                log.LogInformation(ex.StackTrace);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [FunctionName("GetBrandList")]
        public async Task<IActionResult> GetBrandList([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req, ILogger log)
        {
            try
            {
                string token = req.Headers["Authorization"];

                if (token.StartsWith("Bearer"))
                {
                    token = token.Substring(7);
                }

                if (_authorization.ValidateToken(token) && _authorization.HasReadAccess(token))
                {
                    int clientId;
                    try
                    {
                        clientId = int.Parse(req.Query["clientId"]);
                        if (clientId <= 0)
                        {
                            return new BadRequestResult();
                        }
                    }
                    catch (Exception)
                    {
                        return new BadRequestResult();
                    }
                    var brandList = await _service.GetBrandList(clientId);
                    if (brandList.Any())
                    {
                        return new OkObjectResult(JsonConvert.SerializeObject(brandList));
                    }
                    else
                    {
                        return new NotFoundResult();
                    }
                }
                else
                {
                    return new UnauthorizedResult();
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex.Message);
                log.LogInformation(ex.StackTrace);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [FunctionName("AddBrand")]
        public async Task<IActionResult> AddBrand([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req, ILogger log)
        {
            try
            {
                string token = req.Headers["Authorization"];
                if (token.StartsWith("Bearer"))
                {
                    token = token.Substring(7);
                }

                if (_authorization.ValidateToken(token) && _authorization.HasWriteAccess(token))
                {
                    if (!MultipartRequestHelper.IsMultipartContentType(req.ContentType))
                    {
                        return new BadRequestResult();
                    }

                    BrandPostModel brand = await ProcessTask(req);
                    return new OkObjectResult(_service.AddBrand(brand));
                }
                else
                {
                    return new UnauthorizedResult();
                }
            }
            catch(CustomException customEx)
            {
                return new ObjectResult(customEx.Message) { StatusCode = 500 };
            }
            catch(Exception ex)
            {
                log.LogError(ex.InnerException.Message);
                log.LogInformation(ex.StackTrace);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [FunctionName("EditBrand")]
        public async Task<IActionResult> EditBrand([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req, ILogger log)
        {
            try
            {
                string token = req.Headers["Authorization"];
                if (token.StartsWith("Bearer"))
                {
                    token = token.Substring(7);
                }

                if (_authorization.ValidateToken(token) && _authorization.HasWriteAccess(token))
                {
                    BrandPostModel brand = new BrandPostModel();
                    if (!MultipartRequestHelper.IsMultipartContentType(req.ContentType))
                    {
                        return new BadRequestResult();
                    }

                    brand = await ProcessTask(req);
                    return new OkObjectResult(_service.EditBrand(brand));
                }
                else
                {
                    return new UnauthorizedResult();
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex.InnerException.Message);
                log.LogInformation(ex.StackTrace);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        public async Task<BrandPostModel> ProcessTask(HttpRequest req)
        {
            var formModel = new BrandPostModel();
            byte[] fileByte = new byte[0];
            var boundary = MultipartRequestHelper.GetBoundary(MediaTypeHeaderValue.Parse(req.ContentType), new FormOptions().MultipartBoundaryLengthLimit);
            var reader = new MultipartReader(boundary, req.Body);
            var section = await reader.ReadNextSectionAsync();
            while (section != null)
            {
                var hasContentDispositionHeader = ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var contentDisposition);
                if (hasContentDispositionHeader)
                {
                    if (contentDisposition.IsFileDisposition())
                    {
                        // Recommendation is not to trust the file name sent by the client.
                        // To display the file name, HTML-encode the value.
                        var streamedFileContent = await FileHelpers.ProcessStreamedFile(section, contentDisposition, _permittedExtensions, _fileSizeLimit);
                        fileByte = streamedFileContent;
                    }
                    else if (contentDisposition.IsFormDisposition())
                    {
                        var content = new StreamReader(section.Body).ReadToEnd();
                        if (contentDisposition.Name == "BrandDetail")
                        {
                            formModel = JsonConvert.DeserializeObject<BrandPostModel>(content);
                            if (fileByte != null && fileByte.Length > 0)
                            {
                                formModel.file = fileByte;
                            }
                            else
                            {
                                formModel.file = null;
                            }
                        }
                    }
                }
                // Drain any remaining section body that hasn't been consumed and read the headers for the next section.
                section = await reader.ReadNextSectionAsync();
            }
            return formModel;
        }
    }
}
