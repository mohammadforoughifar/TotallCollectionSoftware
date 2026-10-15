using Inventory.Api.Services.Moadian;

using Inventory.Shared.Dtos.Moadian;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Inventory.Api.Infrastructure;
using static Inventory.Shared.Dtos.Moadian.VmInvoiceItems;

namespace Inventory.Api.Controllers.Moadian
{
    // [ApiVersion("1", Deprecated = true)] // حذف شد: API فعلی نسخه‌بندی ندارد

    // ⚠️ راه‌اندازی مسیر: کلاینت همه‌ی فراخوانی‌ها را با پیشوند /Moadian/{action} می‌زند
    // (مثلاً http://localhost:5100/Moadian/SaveCpInfo). بدون این دو اتریبیوت، اکشن‌ها
    // روی مسیر خام خودشان مپ می‌شدند و کلاینت 404 با بدنهٔ خالی می‌گرفت.
    [ApiController]
    [Route("Moadian")]
    [MoadianPermission]
    public class MoadianController(IConnectService connectService, IWebHostEnvironment _env) : BaseApiController
    {
    
        [HttpPost("GetIsConnectToMoadian")]
        public async Task<ApiModel> GetConnect(VMConnection vMConnection)
        {
            // مسیر اصلی: تست با کلید ذخیره‌شدهٔ رکورد اتصال — از wwwroot/uploads/{نام شرکت}
            if (vMConnection is not null && vMConnection.cpinfoid > 0)
                return await connectService.IsConnectStoredAsync(vMConnection.cpinfoid);

            // مسیر سازگاری (درخواست بدون cpinfoid — رفتار قبلی)
            var fileName = vMConnection?.FilePrivatekey?.FileName;
            if (string.IsNullOrEmpty(fileName))
                return new ApiModel { Success = false, Status = 400, Message = "فایل کلید خصوصی یافت نشد — ابتدا اتصال را ذخیره کنید." };
            var filePath = Path.Combine(_env.WebRootPath, "File", "Moadian", fileName);
            var result =await  connectService.IsConnectAsync(vMConnection.TaxMemoryID, filePath, vMConnection.WebServiceAddress);
            return result;
        }

        [HttpDelete("DeleteCpInfo/{id:int}")]
        public async Task<ApiModel> DeleteCpInfo(int id)
        {
            return await connectService.DeleteCpInfoAsync(id);
        }

        [HttpGet("Diagnose")]
        public async Task<ApiModel> Diagnose()
        {
            // گزارش کامل اسکیمای مودیان (وجود جدول‌ها/ستون‌ها + شمار ردیف‌ها) — برای وقتی لیست‌ها خالی می‌آیند
            return await connectService.DiagnoseAsync();
        }
        [HttpGet("DiagnoseLists")]
        public async Task<ApiModel> DiagnoseLists(int cpinfoId)
        {
            // تشخیص عمیق: چرا ردیف در جدول هست ولی لیست‌ها خالی‌اند (دیتابیس متصل + همان کوئری‌های EF)
            return await connectService.DiagnoseListsAsync(cpinfoId);
        }
        [HttpGet("GetConnectForCpInfo/{cpinfoId:int}")]
        public async Task<ApiModel> GetConnectForCpInfo(int cpinfoId)
        {

            return await connectService.GetConnectionApiAsync(cpinfoId);

        }
        [HttpPost("ConnectionApiAsync")]
        public async Task<ApiModel> ConnectionApiAsync(VMConnection model)
        {
            IFormFile peyvastFiles = null;

            if (model.FilePrivatekey!=null)
            {
              
               
                    if (model.FilePrivatekey.FileContent != null)
                    {
                        var stream = new MemoryStream(model.FilePrivatekey.FileContent);
                        var file = new FormFile(stream, 0, stream.Length, model.FilePrivatekey.FileName, model.FilePrivatekey.FileName);
                    peyvastFiles=file;
                    }
                
            }
            var result = await connectService.ConnectionApiAsync(model, peyvastFiles);
            if (result.Success && result.Data is VMConnection resultData)
            {
                //resultData.Links = new List<Links>
                //{
                //    new Links
                //    {
                //       // Href = Url.Action(nameof(GetById),"Madarek",new { version = "1", Id = resultData.MadrakId },Request.Scheme),
                //        Rel = "Self",
                //        Method = "GET"
                //    }
                //};
            }
            return result;
        }
        [HttpGet("GetAllInvoice")]
        public async Task<IResult> GetAllInvoice(DateTime? firstDate, DateTime? endDate, string operation, int CpinfoId, string? searchTerm, string? filterColumn, string? filterValue, string? sortColumn, string? sortOrder, int page, int pageSize)
        {

            var getPageQuery = new GetResultQuery(page: page, pageSize: pageSize, searchTerm: searchTerm, filterColumn: filterColumn, filterValue: filterValue, sortColumn: sortColumn, sortOrder: sortOrder, firstDate: firstDate, endDate: endDate, extra: null, extraId: Guid.Empty);
            var model = await connectService.GetAllInvoiceAsync(operation, CpinfoId, getPageQuery);
            if (model.Success && model.Data is PagedList<UCInvoiceInfo> resultList)
            {
                foreach (var item in resultList.Items)
                {
                    item.Links = new List<Links>
                    {
                        //new Links
                        //{
                        //    Href = Url.Action(nameof(GetById),"Moadian",new { version = "1", Id = item.Id },Request.Scheme),
                        //    Rel = "Self",
                        //    Method = "GET"
                        //},
                        //new Links
                        //{
                        //    Href=Url.Action(nameof(DeleteMadrak),"Moadian",new { version = "1", Id = item.MadrakId },Request.Scheme),
                        //    Rel="Delete",
                        //    Method="Delete"
                        //},
                        //new Links
                        //{
                        //    Href=Url.Action(nameof(EditMadrak),"MadMoadianarek",new { version = "1" },Request.Scheme),
                        //    Rel="Update",
                        //    Method="Put"
                        //}
                    };
                }
            }

            return Results.Ok(model);
        }

        [HttpGet("NextInvoiceNumber")]
        public async Task<ApiModel> NextInvoiceNumber(int cpinfoId)
        {
            return await connectService.NextInvoiceNumberAsync(cpinfoId);
        }

        [HttpPost("AddInvoice")]
        public async Task<ApiModel> AddInvoice(VmInvoiceItems.InvoiceVM model)
        {
          //  var result = await connectService.InsertInvoiceAsync(model);
            var result = await connectService.SabtInvoiceAsync(model);
            if (result.Success && result.Data is InvoiceVM resultData)
            {
            
            }
            return result;
        }  
        
        //[HttpPost("CancelInvoiceAsync")]//ابطال
        //public async Task<ApiModel> CancelInvoiceAsync(VmInvoiceItems.InvoiceVM model)
        //{
        //  //  var result = await connectService.InsertInvoiceAsync(model);
        //    var result = await connectService.CancelInvoiceAsync(model);
        //    if (result.Success && result.Data is InvoiceVM resultData)
        //    {
            
        //    }
        //    return result;
        //}    
        [HttpPost("SendInvoiceAsync")]
        public async Task<ApiModel> SendInvoiceAsync(VmInvoiceItems.InvoiceVM model)
        {
            return await connectService.SendInvoiceAsync(model);
        }

        [HttpPost("PreviewSendInvoiceAsync")]
        public async Task<ApiModel> PreviewSendInvoiceAsync(VmInvoiceItems.InvoiceVM model)
        {
            return await connectService.SendInvoiceAsync(model, previewOnly: true);
        }

        [HttpPost("EslahiInvoiceAsync")]
        public async Task<ApiModel> EslahiInvoiceAsync(VmInvoiceItems.InvoiceVM model)
        {
          //  var result = await connectService.InsertInvoiceAsync(model);
            var result = await connectService.EslahiInvoiceAsync(model);
            if (result.Success && result.Data is InvoiceVM resultData)
            {
            
            }
            return result;
        }
        [HttpPost("EbtalInvoiceAsync")]
        public async Task<ApiModel> EbtalInvoiceAsync(VmInvoiceItems.InvoiceVM model)
        {
            //  var result = await connectService.InsertInvoiceAsync(model);
            var result = await connectService.EbtalInvoiceAsync(model);
            if (result.Success && result.Data is InvoiceVM resultData)
            {
               
            }
            return result;
        }
        [HttpPost("InsertGoodOrServiceAsync")]
        public async Task<ApiModel> InsertGoodOrServiceAsync(VMGoodsOrService model)
        {
            var result = await connectService.InsertGoodOrServiceAsync(model);
            if (result.Success && result.Data is VMGoodsOrService resultData)
            {
                resultData.Links = new List<Links>
                {
                    new Links
                    {
                       Href = Url.Action(nameof(GetGoodOrServiceByIdAsync),"Moadian",new { version = "1", Id = resultData.GoodsOrServicesId },Request.Scheme),
                        Rel = "Self",
                        Method = "GET"
                    }
                };
            }
            return result;
        }
        [HttpPut("UpdateGoodOrService")]
        public async Task<ApiModel> UpdateGoodOrServiceAsync(VMGoodsOrService model)
        {
            var result = await connectService.UpdateGoodOrServiceAsync(model);
            if (result.Success && result.Data is VMGoodsOrService resultData)
            {
                resultData.Links = new List<Links>
                {
                    new Links
                    {
                       Href = Url.Action(nameof(GetGoodOrServiceByIdAsync),"Moadian",new { version = "1", Id = resultData.GoodsOrServicesId },Request.Scheme),
                        Rel = "Self",
                        Method = "GET"
                    }
                };
            }
            return result;
        }
        [HttpGet("UpdateStatusInvoice/{InvoiceId:int}")]
        public async Task<ApiModel> UpdateStatusInvoice(int InvoiceId)
        {
            var result = await connectService.UpdateStatusInvoiceAsync(InvoiceId);
      
            return result;
        }

        [HttpGet("GetGoodOrService")]
        public async Task<IResult> GetGoodOrService(int cpinfoId, string? searchTerm, string? filterColumn, string? filterValue, string? sortColumn, string? sortOrder, int page, int pageSize)
        {
            var getPageQuery = new GetResultQuery(page: page, pageSize: pageSize, searchTerm: searchTerm, filterColumn: filterColumn, filterValue: filterValue, sortColumn: sortColumn, sortOrder: sortOrder, firstDate: null, endDate: null, extra: null, extraId: Guid.Empty);

            var model = await connectService.GetAllGoodOrServiceAsync(cpinfoId,getPageQuery);
            if (model.Success && model.Data is PagedList<VMGoodsOrService> resultList)
            {
                foreach (var item in resultList.Items)
                {
                    item.Links = new List<Links>
                    {
                        new Links
                        {
                            Href = Url.Action(nameof(GetGoodOrServiceByIdAsync),"Moadian",new { version = "1", Id = item.GoodsOrServicesId },Request.Scheme),
                            Rel = "Self",
                            Method = "GET"
                        },
                        //new Links
                        //{
                        //    Href=Url.Action(nameof(DeleteMadrak),"Madarek",new { version = "1", Id = item.MadrakId },Request.Scheme),
                        //    Rel="Delete",
                        //    Method="Delete"
                        //},
                        //new Links
                        //{
                        //    Href=Url.Action(nameof(EditMadrak),"Madarek",new { version = "1" },Request.Scheme),
                        //    Rel="Update",
                        //    Method="Put"
                        //}
                    };
                }
            }

            return Results.Ok(model);
        }

        [HttpGet("GetAllCostomer")]
        public async Task<ApiModel> GetAllCostomer(int cpinfoid)
        {
            return await connectService.GetCustomerForComboAsync(cpinfoid);

        }   
        [HttpGet("EstelamInvoiceAsync")]
        public async Task<ApiModel> EstelamInvoiceAsync(string ReferenceId, int InvoiceId, string dateorder)
        {
            return await connectService.EstelamInvoiceAsync(ReferenceId, InvoiceId, dateorder);

        }  
        [HttpGet("ErrorInvoiceAsync")]
        public async Task<ApiModel> ErrorInvoiceAsync(string ReferenceId)
        {
            return await connectService.ErrorInvoiceAsync(ReferenceId);

        }
        [HttpGet("GetUnitOfMeasurementForComboAsync")]
        public async Task<ApiModel> GetUnitOfMeasurementForComboAsync()
        {
            return await connectService.GetUnitOfMeasurementForComboAsync();

        }
        [HttpGet("GetGoodOrServiceForComboAsync")]
        public async Task<ApiModel> GetGoodOrServiceForComboAsync(int  cpinfoid)
        {
            return await connectService.GetGoodOrServiceForComboAsync(cpinfoid);

        }

        [HttpGet("GetGoodOrServiceByIdAsync/{id:int}")]
        public async Task<ApiModel> GetGoodOrServiceByIdAsync(int id)
        {
            return await connectService.GetGoodOrServiceByIdAsync(id);

        }  
        [HttpGet("GetInvoiceByIdAsync/{id:int}")]
        public async Task<ApiModel> GetInvoiceByIdAsync(int id)
        {
            return await connectService.GetInvoiceByIdAsync(id);

        }
        [HttpPut("UpdateUnitOfMeasurementAsync")]
        public async Task<ApiModel> UpdateUnitOfMeasurementAsync(VMUnitOfMeasurement model)
        {
            var result = await connectService.UpdateUnitOfMeasurementAsync(model);
            if (result.Success && result.Data is VMUnitOfMeasurement resultData)
            {
                resultData.Links = new List<Links>
                {
                    new Links
                    {
                        Href = Url.Action(nameof(GetUnitOfMeasurementByIdAsync),"Moadian",new { version = "1", Id = resultData.UnitOfMeasurementId },Request.Scheme),
                        Rel = "Self",
                        Method = "GET"
                    }
                };
            }
            return result;
        }

        [HttpPost("InsertUnitOfMeasurementAsync")]
        public async Task<ApiModel> InsertUnitOfMeasurementAsync(VMUnitOfMeasurement model)
        {
            var result = await connectService.InsertUnitOfMeasurementAsync(model);
            if (result.Success && result.Data is VMUnitOfMeasurement resultData)
            {
                resultData.Links = new List<Links>
                {
                    new Links
                    {
                 Href = Url.Action(nameof(GetUnitOfMeasurementByIdAsync),"Moadian",new { version = "1", Id = resultData.UnitOfMeasurementId },Request.Scheme),
                        Rel = "Self",
                        Method = "GET"
                    }
                };
            }
            return result;
        }
        [HttpGet("GetUnitOfMeasurementByIdAsync/{id:int}")]
        public async Task<ApiModel> GetUnitOfMeasurementByIdAsync(int id)
        {
            return await connectService.GetUnitOfMeasurementByIdAsync(id);

        }
        [HttpGet("GetAllUnitOfMeasurementAsync")]
        public async Task<IResult> GetAllUnitOfMeasurementAsync(string? searchTerm, string? filterColumn, string? filterValue, string? sortColumn, string? sortOrder, int page, int pageSize)
        {
            var getPageQuery = new GetResultQuery(page: page, pageSize: pageSize, searchTerm: searchTerm, filterColumn: filterColumn, filterValue: filterValue, sortColumn: sortColumn, sortOrder: sortOrder, firstDate: null, endDate: null, extra: null, extraId: Guid.Empty);

            var model = await connectService.GetAllUnitOfMeasurementAsync(getPageQuery);
            if (model.Success && model.Data is PagedList<VMUnitOfMeasurement> resultList)
            {
                foreach (var item in resultList.Items)
                {
                    item.Links = new List<Links>
                    {
                        new Links
                        {
                            Href = Url.Action(nameof(GetUnitOfMeasurementByIdAsync),"Moadian",new { version = "1", Id = item.UnitOfMeasurementId },Request.Scheme),
                            Rel = "Self",
                            Method = "GET"
                        },
                        new Links
                        {
                            Href=Url.Action(nameof(DeleteUnitOfMeasurementByIdAsync),"Moadian",new { version = "1", Id = item.UnitOfMeasurementId },Request.Scheme),
                            Rel="Delete",
                            Method="Delete"
                        },
                        new Links
                        {
                            Href=Url.Action(nameof(UpdateUnitOfMeasurementAsync),"Moadian",new { version = "1" },Request.Scheme),
                            Rel="Update",
                            Method="Put"
                        }
                    };
                }
            }

            return Results.Ok(model);

        }
        [HttpDelete("{id}")]
        public async Task<ApiModel> DeleteUnitOfMeasurementByIdAsync(int id)
        {
            return await connectService.DeleteUnitOfMeasurementByIdAsync(id);

        }
        [HttpDelete("Invoice/{id}")]
        public async Task<ApiModel> DeleteInvoiceByIdAsync(int id)
        {
            return await connectService.DeleteInvoiceByIdAsync(id);

        }

        // ==================== مکمل رابط کاربری (شرکت / سال مالی / مشتری) ====================

        [HttpGet("GetCpInfos")]
        public async Task<ApiModel> GetCpInfos()
        {
            return await connectService.GetAllCpInfosAsync();
        }

        [HttpPost("SaveCpInfo")]
        public async Task<ApiModel> SaveCpInfo(VMShowCpinfoByCompany model)
        {
            return await connectService.SaveCpInfoAsync(model);
        }

        [HttpGet("GetFiscalYears/{cpinfoId:int}")]
        public async Task<ApiModel> GetFiscalYears(int cpinfoId)
        {
            return await connectService.GetAllFiscalYearsAsync(cpinfoId);
        }

        [HttpPost("SaveFiscalYear")]
        public async Task<ApiModel> SaveFiscalYear(VMFiscalYear model)
        {
            return await connectService.SaveFiscalYearAsync(model);
        }

        [HttpPost("InsertCustomerAsync")]
        public async Task<ApiModel> InsertCustomer(VMCustomerList model)
        {
            return await connectService.InsertCustomerAsync(model);
        }

        [HttpPut("UpdateCustomer")]
        public async Task<ApiModel> UpdateCustomer(VMCustomerList model)
        {
            return await connectService.UpdateCustomerAsync(model);
        }
    }
}
