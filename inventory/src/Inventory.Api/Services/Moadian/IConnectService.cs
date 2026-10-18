using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Inventory.Shared.Dtos.Moadian;
using Microsoft.AspNetCore.Http;
using TaxCollectData.Library.Enums;

namespace Inventory.Api.Services.Moadian
{
    public interface IConnectService
    {
        Task<ApiModel> IsConnectAsync(string shenaseyekta,string privatekey,string WebServiceAddress);
        Task<ApiModel> IsConnectStoredAsync(int cpinfoId);//تست اتصال با کلید ذخیره‌شده روی سرور (wwwroot/uploads/{نام شرکت})
        Task<ApiModel> DiagnoseAsync();//تشخیص: وجود جدول‌ها/ستون‌های مودیان و شمار ردیف‌ها — برای وقتی لیست‌ها خالی می‌آیند
        Task<ApiModel> DiagnoseListsAsync(int cpinfoId);//تشخیص عمیق: دیتابیس متصل + نتیجهٔ دقیق همان کوئری‌های لیست با EF

        /// <summary>پیش‌نمایش شمارهٔ صورتحساب بعدی برای فرم ثبت (حداکثر شمارهٔ شرکت + ۱).</summary>
        Task<ApiModel> NextInvoiceNumberAsync(int cpinfoId);
        Task<ApiModel> DeleteCpInfoAsync(int id);//حذف نرم شرکت (و اتصال‌هایش)
        Task<ApiModel> ConnectionApiAsync(VMConnection connection,IFormFile formFile);///اتصال به سامانه
        Task<ApiModel> GetConnectionApiAsync(int cpinfoId);
        Task<ApiModel> InsertGoodOrServiceAsync(VMGoodsOrService modeldata);
        Task<ApiModel> UpdateGoodOrServiceAsync(VMGoodsOrService modeldata);
        Task<ApiModel> GetGoodOrServiceByIdAsync(int Id);
        Task<ApiModel> GetAllGoodOrServiceAsync(int cpinfoId, GetResultQuery request);
        Task<ApiModel> GetGoodOrServiceForComboAsync(int cpinfoid);
        Task<ApiModel> GetCustomerForComboAsync(int cpinfoid);
        Task<ApiModel> GetUnitOfMeasurementForComboAsync(int cpinfoid);
        Task<ApiModel> InsertUnitOfMeasurementAsync(VMUnitOfMeasurement modeldata);
        Task<ApiModel> UpdateUnitOfMeasurementAsync(VMUnitOfMeasurement modeldata);
     
        Task<ApiModel> GetUnitOfMeasurementByIdAsync(int Id);
        Task<ApiModel> GetAllUnitOfMeasurementAsync(int cpinfoId, GetResultQuery request);
        Task<ApiModel> DeleteUnitOfMeasurementByIdAsync(int Id);




        #region صورت حساب
        Task<ApiModel> InsertInvoiceAsync(VmInvoiceItems.InvoiceVM modeldata);
        Task<ApiModel> GetInvoiceByIdAsync(int InvoiceId);
        Task<ApiModel> DeleteInvoiceByIdAsync(int InvoiceId);
        Task<ApiModel> SabtInvoiceAsync(VmInvoiceItems.InvoiceVM modeldata);///صورتحساب جدید و ویرایش صورتحساب
   
        Task<ApiModel> EslahiInvoiceAsync(VmInvoiceItems.InvoiceVM modeldata);///ارسال صورتحساب اصلاحی
        Task<ApiModel> EbtalInvoiceAsync(VmInvoiceItems.InvoiceVM modeldata);///ارسال صورتحساب اصلاحی
        Task<ApiModel> GetAllInvoiceAsync(string operation,int CpinfoId, GetResultQuery request);///نمایش همه صورتحسا بها
        Task<ApiModel> SendInvoiceAsync(VmInvoiceItems.InvoiceVM modelInvoiceVM, bool previewOnly = false);//ارسال یا پیش‌نمایش JSON بدون تماس سامانه
        Task<ApiModel> EstelamInvoiceAsync(string ReferenceId, int InvoiceId,string dateorder);//استعلام صورتحساب
        Task<ApiModel> ErrorInvoiceAsync(string ReferenceId);//لیست خطاهای صورتحساب
        Task<ApiModel> UpdateStatusInvoiceAsync(int InvoiceId);//تغییر وضعیت صورتحساب به مشااهده شده در کارپوشه(ارسال)  صورتحساب
        #endregion

        #region مکمل رابط کاربری (شرکت / سال مالی / مشتری)
        /// <summary>فهرست خدمات‌دهنده‌های مودیانِ یک شرکت داخلی مشخص. اگر شناسه ارسال نشود، شرکت فعال درخواست استفاده می‌شود.</summary>
        Task<ApiModel> GetAllCpInfosAsync(int? companyId = null);
        Task<ApiModel> SaveCpInfoAsync(VMShowCpinfoByCompany model);//ثبت شرکت جدید
        Task<ApiModel> GetAllFiscalYearsAsync(int cpinfoId);//فهرست سال‌های مالی شرکت
        Task<ApiModel> SaveFiscalYearAsync(VMFiscalYear model);//ثبت سال مالی جدید
        Task<ApiModel> InsertCustomerAsync(VMCustomerList model);//ثبت مشتری جدید
        Task<ApiModel> UpdateCustomerAsync(VMCustomerList model);//ویرایش مشتری
        #endregion
    }
}
