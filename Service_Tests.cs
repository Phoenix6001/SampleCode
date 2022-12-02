using BrandFunction.Data.Interfaces;
using BrandFunction.Data.Models;
using BrandFunction.Domain;
using BrandFunction.Domain.Models;
using Microsoft.AspNetCore.Http;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace BrandFunction.Test
{
    public class Service_Tests
    {
        Mock<IDataAccess> mockDataAccess = new Mock<IDataAccess>();

        [Fact]
        public async void PrepareBrand_ReturnsPrepareList()
        {
            var service = new Service(mockDataAccess.Object);
            BrandPrepareModel brandPrepare = new BrandPrepareModel();
            List<Segment> segments = new List<Segment>()
            {
                new Segment() { SegmentId = 1, Name = "Test Segment", Abbr = "TS" },
                new Segment() { SegmentId = 2, Name = "Test Segment 2", Abbr = "TS2" }
            };
            List<Cuisine> cuisines = new List<Cuisine>()
            {
                new Cuisine() { CuisineId = 1, Name = "Test Cuisine", Abbr = "TC1" },
                new Cuisine() { CuisineId = 2, Name = "Test Cuisine 2", Abbr = "TC2"}
            };
            List<Brand> brandsList = new List<Brand>()
            {
               new Brand()
               {
                    BrandId = 1,
                    Name = "Brand",
                    CuisineId = 1,
                    Cuisine = new Cuisine() { CuisineId = 1, Name = "Cuisine", Abbr = "Cu" },
                    SegmentId = 1,
                    Segment = new Segment() { SegmentId = 1, Name = "Segment", Abbr = "Se" },
                    ClientId = 1,
                    ActiveStatus = true
               },
               new Brand()
               {
                    BrandId = 2,
                    Name = "Brand11",
                    CuisineId = 1,
                    Cuisine = new Cuisine() { CuisineId = 1, Name = "Cuisine", Abbr = "Cu" },
                    SegmentId = 1,
                    Segment = new Segment() { SegmentId = 1, Name = "Segment", Abbr = "Se" },
                    ClientId = 1,
                    ActiveStatus = true
               }
            };
            List<BrandViewModel> competitorBrands = brandsList.Select(x => new BrandViewModel()
            {
                BrandId = x.BrandId,
                Name = x.Name,
                ClientId = x.ClientId,
                Segment = x.Segment,
                Cuisine = x.Cuisine,
                ActiveStatus = true
            }).ToList();

            mockDataAccess.Setup(x => x.GetAllCuisines()).ReturnsAsync(cuisines);
            mockDataAccess.Setup(x => x.GetAllSegments()).ReturnsAsync(segments);
            mockDataAccess.Setup(x => x.GetAllBrands()).ReturnsAsync(brandsList);

            brandPrepare.Segments = segments;
            brandPrepare.Cuisines = cuisines;
            brandPrepare.CompetitorBrands = competitorBrands;

            BrandPrepareModel result = await service.PrepareBrand();

            Assert.Equal(2, result.Segments.Count);
            Assert.Equal(2, result.Cuisines.Count);
            Assert.Equal(2, result.CompetitorBrands.Count);
        }

        [Fact]
        public async void GetBrandList_ReturnsBrandsList()
        {
            var service = new Service(mockDataAccess.Object);
            int clientId = 1;
            List<Brand> brandList = new List<Brand>()
            {
                new Brand() { BrandId = 1, Name = "Brand", ClientId = 1, SegmentId = 1, CuisineId = 1, ActiveStatus = true }
            };

            mockDataAccess.Setup(x => x.GetAllClientBrands(clientId)).ReturnsAsync(brandList);
            IEnumerable<BrandViewModel> result = await service.GetBrandList(1);

            Assert.Equal(brandList[0].BrandId, result.ToList()[0].BrandId);
        }

        [Fact]
        public void AddBrand_InsertCalled()
        {
            var service = new Service(mockDataAccess.Object);

            List<Brand> existingBrands = new List<Brand>() { };
            mockDataAccess.Setup(x => x.GetBrandByNameAndClient("Brand", 0, 1, "Add")).ReturnsAsync(existingBrands);

            var fileMock = new Mock<IFormFile>();
            var ms = new MemoryStream();
            var fileName = "test.jpg";
            fileMock.Setup(_ => _.OpenReadStream()).Returns(ms);
            fileMock.Setup(_ => _.FileName).Returns(fileName);
            fileMock.Setup(_ => _.Length).Returns(ms.Length);

            Environment.SetEnvironmentVariable("BLOB_STORAGE_CONN_STRING", "endpoints");
            BrandPostModel request = new BrandPostModel() { Name = "Brand", ClientId = 1, CuisineId = 1, SegmentId = 1, ActiveStatus = true, file = new byte[] { 0x00, 0x01, 0x02, 0x03 } };
            mockDataAccess.Setup(x => x.InsertBrand(It.IsAny<Brand>()));

            service.AddBrand(request);

            mockDataAccess.Verify(x => x.InsertBrand(It.Is<Brand>(y => y.Name == "Brand" && y.ClientId == 1 && y.CuisineId == 1 && y.SegmentId == 1)));
        }

        [Fact]
        public void UpdateBrand_UpdateExistingBrand_ReturnsSuccess()
        {
            var service = new Service(mockDataAccess.Object);
            var brand = new Brand() { BrandId = 1, Name = "Brand", ClientId = 1, CuisineId = 1, SegmentId = 1, ActiveStatus = true };
            
            List<Brand> existingBrands = new List<Brand>() { };
            mockDataAccess.Setup(x => x.GetBrandByNameAndClient("Brand", 1, 1, "Edit")).ReturnsAsync(existingBrands);
            mockDataAccess.Setup(x => x.GetBrandById(1)).Returns(brand);

            List<int> segmentIds = new List<int> { 1, 2 };
            List<int> competitorBrandIds = new List<int> { 1 };
            mockDataAccess.Setup(x => x.GetAvailableSegmentIdsByBrandId(brand.BrandId)).Returns(segmentIds);
            mockDataAccess.Setup(x => x.GetCompetitorBrandIdsByBrandId(brand.BrandId)).Returns(competitorBrandIds);
            mockDataAccess.Setup(x => x.DeleteAvailableSegments(segmentIds, brand.BrandId));
            mockDataAccess.Setup(x => x.DeleteBrandCompitetorBrands(competitorBrandIds, brand.BrandId));
            mockDataAccess.Setup(x => x.FusionContextSaveChanges());

            BrandPostModel request = new BrandPostModel() { BrandId = 1, Name = "Brand", ClientId = 1, CuisineId = 1, SegmentId = 1, ActiveStatus = true, file = new byte[] { 0x00, 0x01, 0x02, 0x03 }, AvailableSegments = segmentIds, CompetitorBrands = competitorBrandIds };

            Environment.SetEnvironmentVariable("BLOB_STORAGE_CONN_STRING", "endpoints");
            var result = service.EditBrand(request);

            mockDataAccess.Verify(x => x.FusionContextSaveChanges(), Times.Once);
            Assert.Equal("Success", result);
        }
    }
}
