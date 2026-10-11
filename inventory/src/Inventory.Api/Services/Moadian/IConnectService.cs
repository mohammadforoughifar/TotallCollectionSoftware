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
        Task<ApiModel> ConnectionApiAsync(VMConnection connection,IFormFile formFile);///اتصال به سامانه
        Task<ApiModel> GetConnectionApiAsync(int cpinfoId);
        Task<ApiModel> InsertGoodOrServiceAsync(VMGoodsOrService modeldata);
        Task<ApiModel> UpdateGoodOrServiceAsync(VMGoodsOrService modeldata);
        Task<ApiModel> GetGoodOrServiceByIdAsync(int Id);
        Task<ApiModel> GetAllGoodOrServiceAsync(int cpinfoId, GetResultQuery request);
        Task<ApiModel> GetGoodOrServiceForComboAsync(int cpinfoid);
        Task<ApiModel> GetCustomerForComboAsync(int cpinfoid);
        Task<ApiModel> GetUnitOfMeasurementForComboAsync();
        Task<ApiModel> InsertUnitOfMeasurementAsync(VMUnitOfMeasurement modeldata);
        Task<ApiModel> UpdateUnitOfMeasurementAsync(VMUnitOfMeasurement modeldata);
     
        Task<ApiModel> GetUnitOfMeasurementByIdAsync(int Id);
        Task<ApiModel> GetAllUnitOfMeasurementAsync(GetResultQuery request);
        Task<ApiModel> DeleteUnitOfMeasurementByIdAsync(int Id);




        #region صورت حساب
        Task<ApiModel> InsertInvoiceAsync(VmInvoiceItems.InvoiceVM modeldata);
        Task<ApiModel> GetInvoiceByIdAsync(int InvoiceId);
        Task<ApiModel> DeleteInvoiceByIdAsync(int InvoiceId);
        Task<ApiModel> SabtInvoiceAsync(VmInvoiceItems.InvoiceVM modeldata);///صورتحساب جدید و ویرایش صورتحساب
   
        Task<ApiModel> EslahiInvoiceAsync(VmInvoiceItems.InvoiceVM modeldata);///ارسال صورتحساب اصلاحی
        Task<ApiModel> EbtalInvoiceAsync(VmInvoiceItems.InvoiceVM modeldata);///ارسال صورتحساب اصلاحی
        Task<ApiModel> GetAllInvoiceAsync(string operation,int CpinfoId, GetResultQuery request);///نمایش همه صورتحسا بها
        Task<ApiModel> SendInvoiceAsync(VmInvoiceItems.InvoiceVM modelInvoiceVM);//ارسال و عملیات در کارپوشه از فرم همه صورتحسابها
        Task<ApiModel> EstelamInvoiceAsync(string ReferenceId, int InvoiceId,string dateorder);//استعلام صورتحساب
        Task<ApiModel> ErrorInvoiceAsync(string ReferenceId);//لیست خطاهای صورتحساب
        Task<ApiModel> UpdateStatusInvoiceAsync(int InvoiceId);//تغییر وضعیت صورتحساب به مشااهده شده در کارپوشه(ارسال)  صورتحساب
        #endregion

        #region مکمل رابط کاربری (شرکت / سال مالی / مشتری)
        Task<ApiModel> GetAllCpInfosAsync();//فهرست شرکت‌های تعریف‌شده در مودیان
        Task<ApiModel> SaveCpInfoAsync(VMShowCpinfoByCompany model);//ثبت شرکت جدید
        Task<ApiModel> GetAllFiscalYearsAsync(int cpinfoId);//فهرست سال‌های مالی شرکت
        Task<ApiModel> SaveFiscalYearAsync(VMFiscalYear model);//ثبت سال مالی جدید
        Task<ApiModel> InsertCustomerAsync(VMCustomerList model);//ثبت مشتری جدید
        Task<ApiModel> UpdateCustomerAsync(VMCustomerList model);//ویرایش مشتری
        #endregion
    }
}
