 using BrandFunction.Data.Interfaces;
using BrandFunction.Data.Models;
using BrandFunction.Domain.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.IO;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using RestSharp;

namespace BrandFunction.Domain
{
    public class Service : IService
    {
        private readonly IDataAccess _dataAccess;

        public Service(IDataAccess dataAccess)
        {
            _dataAccess = dataAccess;
        }

        public async Task<IEnumerable<BrandViewModel>> GetBrandList(int clientId)
        {
            var brandList = await _dataAccess.GetAllClientBrands(clientId);
            return brandList?.Select(b => new BrandViewModel()
            {
                    BrandId = b.BrandId,
                    Name = b.Name,
                    Brand_Id = b.Brand_Id,
                    Segment = b.Segment,
                    Cuisine = b.Cuisine,
                    ClientId = b.ClientId,
                    ActiveStatus = b.ActiveStatus,
                    IsClientBrand = b.IsClientBrand,
                    IsCompetitorBrand = b.IsCompetitorBrand,
                    BrandLogo = new BrandLogo { Name = b.Name, Logo = GetBrandLogo(b.Name, b.ClientId) },
                    AvailableSegments = b.BrandAvailableSegments?.Select(s => s.Segment)?.ToList(),
                    CompetitorBrands = b.BrandCompetitorBrand?.Select(br => new BrandViewModel() { 
                        BrandId = br.CompetitorBrand.BrandId,
                        Name = br.CompetitorBrand.Name,
                        Logo = GetBrandLogo(br.CompetitorBrand.Name, br.CompetitorBrand.ClientId)
                    })?.ToList()

            });
        }

        public string AddBrand(BrandPostModel brandPostModel)
        {
            var checkExistingBrand = _dataAccess.GetBrandByNameAndClient(brandPostModel.Name, 0, brandPostModel.ClientId, "Add").Result;
            if (checkExistingBrand.Count > 0)
            {
                return "Brand name already exists for this client please try again";
            }
            Brand brand = new Brand()
            {
                Name = brandPostModel.Name,
                ClientId = brandPostModel.ClientId,
                SegmentId = brandPostModel.SegmentId,
                CuisineId = brandPostModel.CuisineId,
                ActiveStatus = brandPostModel.ActiveStatus,
                IsClientBrand = brandPostModel.IsClientBrand,
                IsCompetitorBrand = brandPostModel.IsCompetitorBrand,
                BrandAvailableSegments = brandPostModel.AvailableSegments?.Select(b => new BrandAvailableSegment() { SegmentId = b }).ToList(),
                BrandCompetitorBrand = brandPostModel.CompetitorBrands?.Select(c => new BrandCompetitorBrand() { CompetitorBrandId = c }).ToList()
             };

            if(!string.IsNullOrEmpty(brandPostModel.Brand_Id))
                brand.Brand_Id = brandPostModel.Brand_Id;

             _dataAccess.InsertBrand(brand);
            string result = "Success";
            if (result == "Success")
            {
                Upload(brandPostModel);
            }
            return "Success";
        }

        public string EditBrand(BrandPostModel brandPostModel)
        {
            string brandNameReserved = "";
            var checkExistingBrand = _dataAccess.GetBrandByNameAndClient(brandPostModel.Name, brandPostModel.BrandId, brandPostModel.ClientId, "Edit").Result;
            if (checkExistingBrand.Any())
            {
                return "Brand name already exists for this client please try again";
            }
            var brand = _dataAccess.GetBrandById(brandPostModel.BrandId);
            brandNameReserved = brand.Name;

            if (brand == null) return "Brand Not Found";

            brand.Name = brandPostModel.Name;
            brand.Brand_Id = brandPostModel.Brand_Id;
            brand.ClientId = brandPostModel.ClientId;
            brand.SegmentId = brandPostModel.SegmentId;
            brand.CuisineId = brandPostModel.CuisineId;
            brand.ActiveStatus = brandPostModel.ActiveStatus;
            brand.IsClientBrand = brandPostModel.IsClientBrand;
            brand.IsCompetitorBrand = brandPostModel.IsCompetitorBrand;


            var segmentIds = _dataAccess.GetAvailableSegmentIdsByBrandId(brandPostModel.BrandId);
            var competitorBrandIds = _dataAccess.GetCompetitorBrandIdsByBrandId(brandPostModel.BrandId);

            var segmentIdsToBeRemoved = segmentIds?.Except(brandPostModel.AvailableSegments).ToList();
            var previouslyInsertedSegmentIds = segmentIds?.Intersect(brandPostModel.AvailableSegments).ToList();

            brandPostModel.AvailableSegments?.RemoveAll(t => previouslyInsertedSegmentIds.Contains(t));

            var competitorBrandIdsToBeRemoved = competitorBrandIds?.Except(brandPostModel.CompetitorBrands)?.ToList();
            var previouslyInsertedCpmetitorBrandIds = competitorBrandIds?.Intersect(brandPostModel.CompetitorBrands)?.ToList();

            brandPostModel.CompetitorBrands?.RemoveAll(t => previouslyInsertedCpmetitorBrandIds.Contains(t));

            // Remove From DB
            _dataAccess.DeleteAvailableSegments(segmentIdsToBeRemoved, brandPostModel.BrandId);
            _dataAccess.DeleteBrandCompitetorBrands(competitorBrandIdsToBeRemoved, brandPostModel.BrandId);

            brand.BrandAvailableSegments = brandPostModel.AvailableSegments?.Select(b => new BrandAvailableSegment() { SegmentId = b }).ToList();
            brand.BrandCompetitorBrand = brandPostModel.CompetitorBrands?.Select(c => new BrandCompetitorBrand() { CompetitorBrandId = c }).ToList();

            _dataAccess.FusionContextSaveChanges();

            //
            if (brandPostModel.IsLogoDeleted)
            {
                DeleteLogo(brandPostModel.Name, brandPostModel.ClientId);
            }
            if (brandPostModel.file != null)
            {
                Upload(brandPostModel);
            }
            if(brandNameReserved != brandPostModel.Name)
            {
                //rename the logo file
                RenameLogo(brandNameReserved, brandPostModel.Name, brandPostModel.ClientId);
            }
            
            return "Success";
        }

        public async Task<BrandPrepareModel> PrepareBrand()
        {
            BrandPrepareModel brandPrepare = new BrandPrepareModel();
            brandPrepare.Segments = await _dataAccess.GetAllSegments();
            brandPrepare.Cuisines = await _dataAccess.GetAllCuisines();
            var brandList = await _dataAccess.GetAllBrands();
            var competitorBrands = brandList?.Select(b => new BrandViewModel()
            {
                BrandId = b.BrandId,
                Name = b.Name,
                Segment = b.Segment,
                Cuisine = b.Cuisine,
                ClientId = b.ClientId,
                ActiveStatus = b.ActiveStatus
            }).ToList();
            brandPrepare.CompetitorBrands = competitorBrands;

            return brandPrepare;
        }

        private static async void Upload(BrandPostModel brandPostModel)
        {
            try
            {
                var connectionString = Environment.GetEnvironmentVariable("BLOB_STORAGE_CONN_STRING");
                var container = Environment.GetEnvironmentVariable("BLOB_CONTAINER");

                BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);
                BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(container);

                string blobName = brandPostModel.Name + "-" + brandPostModel.ClientId;
                var blobClient = containerClient.GetBlobClient(blobName);
                await blobClient.DeleteIfExistsAsync();

                using (MemoryStream stream = new MemoryStream(brandPostModel.file))
                {
                    containerClient.UploadBlob(blobName, stream);
                }
            }
            catch(Exception ex)
            {

            }
        }

        private static async void DeleteLogo(string name, int clientId)
        {
            var connectionString = Environment.GetEnvironmentVariable("BLOB_STORAGE_CONN_STRING");
            var container = Environment.GetEnvironmentVariable("BLOB_CONTAINER");

            BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(container);

            string blobName = name + "-" + clientId;
            var blobClient = containerClient.GetBlobClient(blobName);
            await blobClient.DeleteIfExistsAsync();
        }

        public static async void RenameLogo(string oldName, string newName, int clientId)
        {
            var connectionString = Environment.GetEnvironmentVariable("BLOB_STORAGE_CONN_STRING");
            var container = Environment.GetEnvironmentVariable("BLOB_CONTAINER");

            BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(container);

            oldName = oldName + "-" + clientId;
            BlobClient sourceBlob = containerClient.GetBlobClient(oldName);
            BlobLeaseClient lease = sourceBlob.GetBlobLeaseClient();
            await lease.AcquireAsync(TimeSpan.FromSeconds(-1));

            //get the source blob's properties and display the lease state
            BlobProperties sourceProperties = await sourceBlob.GetPropertiesAsync();

            //get a blobclient representing the destination blob
            newName = newName + "-" + clientId;
            BlobClient destBlob = containerClient.GetBlobClient(newName);

            //start the copy operation
            await destBlob.StartCopyFromUriAsync(sourceBlob.Uri);

            //update the source blobl's properties
            sourceProperties = await sourceBlob.GetPropertiesAsync();

            if (sourceProperties.LeaseState == LeaseState.Leased)
            {
                //break the lease on the source blob
                await lease.BreakAsync();

                //update the source blob's properties to check the lease state
                sourceProperties = await sourceBlob.GetPropertiesAsync();
            }
            await sourceBlob.DeleteAsync(DeleteSnapshotsOption.IncludeSnapshots);
        }

        private byte[] GetBrandLogo(string name, int clientId)
        {
            try
            {
                var connectionString = Environment.GetEnvironmentVariable("BLOB_STORAGE_CONN_STRING");
                var container = Environment.GetEnvironmentVariable("BLOB_CONTAINER");

                BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);
                var containerClient = blobServiceClient.GetBlobContainerClient(container);
                name = name + "-" + clientId;
                var blobClient = containerClient.GetBlobClient(name);

                if (blobClient.ExistsAsync().Result)
                {
                    using (var stream = new MemoryStream())
                    {
                        blobClient.DownloadTo(stream);
                        return stream.ToArray();
                    }
                }
                return new byte[0];
            }
            catch(Exception ex)
            {
                return new byte[0];
            }
        }
        public IRestResponse ValidateToken(string token)
        {
            var client = new RestClient(Environment.GetEnvironmentVariable("OKTA_INTROSPECTION_ENDPOINT") + "?client_id=" + Environment.GetEnvironmentVariable("OKTA_CLIENT_ID"))
            {
                Timeout = -1
            };
            var request = new RestRequest(Method.POST);
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Content-Type", "application/x-www-form-urlencoded");
            request.AddParameter("token", token);
            request.AddParameter("token_type_hint", "access_token");
            return client.Execute(request);
        }
    }
}
