
using Inventory.Api.Services.Moadian;
using Inventory.Shared.Dtos.Moadian;

using Inventory.Api.Helpers.Moadian;
using Inventory.Api.Data;


using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using TaxCollectData.Library.Abstraction;
using TaxCollectData.Library.Business;
using TaxCollectData.Library.Dto.Config;
using TaxCollectData.Library.Dto.Content;
using TaxCollectData.Library.Dto.Properties;
using TaxCollectData.Library.Enums;
using static Inventory.Shared.Dtos.Moadian.VmInvoiceItems;





namespace Inventory.Api.Services.Moadian
{
    public class ConnectService(AppDbContext context, IHttpContextAccessor httpContextAccessor) : IConnectService
    {
        // ================== تفکیک بر اساس شرکت فعال کاربر ==================
        // هر خدمات‌دهندهٔ مودیان (CpInfo) به یک شرکت داخلی سیستم (SystemCompany) وصل است.
        // کاربر — حتی Admin — فقط خدمات‌دهنده‌های «شرکت فعال» خودش را می‌بیند و مدیریت می‌کند.
        private int CurrentUserId
        {
            get
            {
                var v = httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
                return int.TryParse(v, out var id) ? id : 0;
            }
        }

        private string CurrentUsername => httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Name) ?? "";

        private bool IsAdminUser => httpContextAccessor.HttpContext?.User?.IsInRole("Admin") == true;

        /// <summary>شرکت فعال کاربر — همان منطق CompanyContextController: هدر X-Company-Id اگر در
        /// دسترسی‌های کاربر باشد، وگرنه شرکت پیش‌فرض (IsDefault) و در نبود آن اولین شرکت مجاز.
        /// برای Admin بدون ردیف دسترسی، همهٔ شرکت‌های فعال مجازند (فعال = انتخاب‌شده در نوار بالا).</summary>
        private async Task<int?> ResolveActiveCompanyIdAsync()
        {
            var userId = CurrentUserId;
            int? requested = null;
            var header = httpContextAccessor.HttpContext?.Request?.Headers["X-Company-Id"].FirstOrDefault();
            if (int.TryParse(header, out var h) && h > 0) requested = h;

            var access = await context.UserCompanyAccesses.AsNoTracking()
                .Where(x => x.UserId == userId && x.Company.IsActive)
                .OrderByDescending(x => x.IsDefault).ThenBy(x => x.Company.Name).ThenBy(x => x.CompanyId)
                .Select(x => x.CompanyId).ToListAsync();
            if (access.Count > 0)
                return requested is > 0 && access.Contains(requested.Value) ? requested : access[0];

            // سازگاری قدیمی: شرکت روی خود کاربر (SystemUsers.CompanyId)
            var legacy = await context.SystemUsers.AsNoTracking()
                .Where(u => u.Username == CurrentUsername && u.CompanyId != null)
                .Select(u => u.CompanyId).FirstOrDefaultAsync();
            if (legacy is > 0) return legacy;

            if (IsAdminUser)
            {
                var companies = await context.SystemCompanies.AsNoTracking()
                    .Where(c => c.IsActive).OrderBy(c => c.Name).ThenBy(c => c.Id)
                    .Select(c => c.Id).ToListAsync();
                if (companies.Count > 0)
                    return requested is > 0 && companies.Contains(requested.Value) ? requested : companies[0];
            }
            return null;
        }

        /// <summary>شرکت‌هایی که کاربر می‌تواند برای خدمات‌دهنده انتخاب کند (دسترسی‌هایش؛ Admin بدون دسترسی = همه).</summary>
        private async Task<List<int>> AccessibleCompanyIdsAsync()
        {
            var userId = CurrentUserId;
            var access = await context.UserCompanyAccesses.AsNoTracking()
                .Where(x => x.UserId == userId && x.Company.IsActive)
                .Select(x => x.CompanyId).ToListAsync();
            if (access.Count > 0) return access;

            var legacy = await context.SystemUsers.AsNoTracking()
                .Where(u => u.Username == CurrentUsername && u.CompanyId != null)
                .Select(u => u.CompanyId).FirstOrDefaultAsync();
            if (legacy is > 0) return new List<int> { legacy.Value };

            if (IsAdminUser)
                return await context.SystemCompanies.AsNoTracking().Where(c => c.IsActive).Select(c => c.Id).ToListAsync();
            return new List<int>();
        }

        /// <summary>شناسهٔ خدمات‌دهنده‌هایی که کاربر در شرکت فعالش مجاز به دیدن/مدیریت آن‌هاست.</summary>
        private async Task<HashSet<int>> AllowedCpInfoIdsAsync()
        {
            var active = await ResolveActiveCompanyIdAsync();
            if (active is null) return new HashSet<int>();
            var ids = await context.CPInfos.AsNoTracking()
                .Where(c => !c.IsDelete && c.CompanyId == active.Value)
                .Select(c => c.Id).ToListAsync();
            return ids.ToHashSet();
        }

        /// <summary>اگر خدمات‌دهنده برای شرکت فعال کاربر مجاز نباشد، پاسخ خطای آماده برمی‌گردد؛ وگرنه null.</summary>
        private async Task<ApiModel?> GuardCpInfoAsync(int cpInfoId)
        {
            if (cpInfoId <= 0) return null;
            var allowed = await AllowedCpInfoIdsAsync();
            if (allowed.Contains(cpInfoId)) return null;
            return new ApiModel
            {
                Data = null,
                Status = 403,
                Success = false,
                Message = "این خدمات‌دهنده مربوط به شرکت فعال شما نیست؛ ابتدا شرکت فعال را از نوار بالای سیستم تغییر دهید یا از مدیر بخواهید این خدمات‌دهنده را به شرکت شما وصل کند."
            };
        }

        public async Task<ApiModel> GetAllGoodOrServiceAsync(int cpinfoId, GetResultQuery request)
        {
            if (await GuardCpInfoAsync(cpinfoId) is { } guard) return guard;
            ApiModel model = new ApiModel();

            try
            {

                var query = context.GoodsOrServices.Where(c => c.CpInfo == cpinfoId && !c.IsDelete).AsQueryable();



                // جستجو مدارک بر اساس نام، شماره یکتا  
                if (!string.IsNullOrEmpty(request.SearchTerm))
                {
                    query = query.Where(c => c.Name.Trim().Contains(request.SearchTerm) || c.UniqueIdentifier.Trim().Contains(request.SearchTerm));
                }

                //Order => صعودی یا نزولی desc/asc
                //بعد از آن مرتب کن بر اساس ستونی که کاربر می دهد SortColumn
                if (request.SortColumn != null)
                {
                    if (request.SortOrder?.ToLower() == "desc")
                    {
                        query = request.SortColumn.ToLower() switch
                        {
                            "Price" => query.OrderByDescending(x => x.Price),
                            "UniqueIdentifier" => query.OrderByDescending(x => x.UniqueIdentifier),


                            _ => query.OrderByDescending(x => x.Id)
                        };
                    }
                    else
                    {
                        query = request.SortColumn.ToLower() switch
                        {
                            "Price" => query.OrderBy(x => x.Price),
                            "UniqueIdentifier" => query.OrderBy(x => x.UniqueIdentifier),


                            _ => query.OrderBy(x => x.Id)
                        };
                    }
                }
                else
                {
                    query = query.OrderBy(x => x.Id);
                }

                var result = query.Select(c => new VMGoodsOrService()
                {
                    GoodsOrServicesId = c.Id,
                    Name = c.Name,
                    UniqueIdentifier = c.UniqueIdentifier,
                    UnitOfMeasurement = c.UnitOfMeasurement,
                    Price = c.Price,
                    CpInfoId = c.CpInfo,
                    ValueAddedPercentage = c.ValueAddedPercentage,


                });

                var data = await PagedList<VMGoodsOrService>.CreateAsync(result, (int)request.Page, (int)request.PageSize);

                model.Data = data;
                model.Status = 200;
                model.Success = true;
                model.Message = MessageCode.StatusCode.TryGetValue(200, out string messager) == true
                    ? model.Message = messager
                    : model.Message = "کد وضعیت نامعتبر است.";
                return model;
            }
            catch (Exception exception)
            {
                model.Data = null;
                model.Message = MessageCode.StatusCode.TryGetValue(500, out string messager) == true
                    ? model.Message = messager
                    : model.Message = exception.Message;
                model.Status = 500;
                model.Success = false;
                return model;
            }
        }

        public async Task<ApiModel> InsertGoodOrServiceAsync(VMGoodsOrService modeldata)
        {
            if (await GuardCpInfoAsync(modeldata.CpInfoId) is { } guard) return guard;
            ApiModel model = new ApiModel();
            if (modeldata.UniqueIdentifier == "" || modeldata.Price == 0 ||
            modeldata.Name == "")
            {
                model.Data = null;
                model.Status = 400;
                model.Message = "نام کالا، شناسهٔ CPC و قیمت واحد (بیشتر از صفر) الزامی است.";
                model.Success = false;
                return model;
            }
            else
            {
                try
                {
                    // واحد سنجش اختیاری است (فرم «— انتخاب —» با مقدار 0 می‌فرستد):
                    // FirstOrDefault(...).Name بدون نال‌چک → NullReferenceException → 500؛ اینجا ایمن می‌گیریم.
                    var unit = modeldata.UnitOfMeasurementId > 0
                        ? context.UnitOfMeasurements.FirstOrDefault(c => c.Id == modeldata.UnitOfMeasurementId)?.Name ?? modeldata.UnitOfMeasurement
                        : modeldata.UnitOfMeasurement;
                    var result = new GoodsOrService()
                    {
                        UnitOfMeasurement = unit,
                        Name = modeldata.Name,
                        UniqueIdentifier = modeldata.UniqueIdentifier,
                        ValueAddedPercentage = Convert.ToByte(Math.Clamp(modeldata.ValueAddedPercentage ?? 0, 0, 100)),
                        Price = modeldata.Price,
                        CpInfo = (int)modeldata.CpInfoId,
                    };

                    await context.AddAsync(result);
                    await context.SaveChangesAsync();

                    model.Data = result;
                    model.Status = 201;
                    model.Message = MessageCode.StatusCode.TryGetValue(201, out string messager) == true
                        ? model.Message = messager
                        : model.Message = "کد وضعیت نامعتبر است.";
                    model.Success = true;

                    return model;

                }
                catch (Exception)
                {
                    model.Data = null;
                    model.Status = 500;
                    model.Message = MessageCode.StatusCode.TryGetValue(500, out string messager) == true
                        ? model.Message = messager
                        : model.Message = "کد وضعیت نامعتبر است.";
                    model.Success = false;

                    return model;
                }
            }
        }

        private ConnectInfo ConnectInfo(int incpiid)
        {
            return context.ConnectInfos.FirstOrDefault(c => c.CpInfo == incpiid && c.IsDelete == false);
        }
        public async Task<ApiModel> InsertInvoiceAsync(VmInvoiceItems.InvoiceVM modeldata)
        {
            ApiModel model = new ApiModel();
            if (modeldata.PaymentType == "" || (!IsNoBuyerDisclosureType(SafeInvoiceType(TypePatternToCode(modeldata.TypePattern))) && (modeldata.CustomerId == null || modeldata.CustomerId == 0)) || modeldata.StatusSentToSystem == "" ||
            modeldata.InvoiceItems == null)
            {
                model.Data = null;
                model.Status = 400;
                model.Message = MessageCode.StatusCode.TryGetValue(400, out string messager) == true
                    ? model.Message = messager
                    : model.Message = "کد وضعیت نامعتبر است.";
                model.Success = false;
                return model;
            }
            else
            {

                try
                {
                    List<InvoiceItem> list = new List<InvoiceItem>();
                    if (modeldata.IsNew)
                    {
                        var invoice = new Invoice()
                        {
                            CustomerId = IsNoBuyerDisclosureType(SafeInvoiceType(TypePatternToCode(modeldata.TypePattern))) ? null : modeldata.CustomerId,
                            DateOrder = DateOnly.FromDateTime(DateTime.Now),
                            CpInfo = modeldata.CPInfoId,
                            PaymentType = modeldata.PaymentType,
                            Description = modeldata.Description,
                            StatusSentToSystem = modeldata.StatusSentToSystem,
                            ShowButtons = true,
                            Subject = "اصلی",

                        };

                        await context.AddAsync(invoice);
                        await context.SaveChangesAsync();

                        if (modeldata.InvoiceItems.Count > 0)
                        {
                            foreach (var item in modeldata.InvoiceItems)
                            {
                                InvoiceItem invoiceItem2 = new InvoiceItem()
                                {
                                    InvoiceId = invoice.Id,
                                    GoodsOrServicesId = (int)item.GoodsOrServicesId,
                                    UniqueIdentifier = item.UniqueIdentifier,
                                    Count = (decimal)item.Count,

                                    PriceUnit = (int)item.PriceUnit,
                                    ValueAddedPercentage = Convert.ToInt32(item.ValueAddedPercentage),
                                    Discount = (decimal)item.Discount,
                                };
                                list.Add(invoiceItem2);


                            }
                            await context.InvoiceItems.AddRangeAsync(list);
                            await context.SaveChangesAsync();
                            model.Data = modeldata;
                            model.Status = 201;
                            model.Message = MessageCode.StatusCode.TryGetValue(201, out string messageradd) == true
                                ? model.Message = messageradd
                                : model.Message = "کد وضعیت نامعتبر است.";
                            model.Success = true;

                            return model;
                        }
                    }
                    else
                    {
                        var invoice = await context.Invoices.FindAsync(modeldata.Id);
                        if (invoice is not null)
                        {
                            invoice.CustomerId = IsNoBuyerDisclosureType(SafeInvoiceType(TypePatternToCode(modeldata.TypePattern))) ? null : modeldata.CustomerId;

                            invoice.PaymentType = modeldata.PaymentType;
                            invoice.Description = modeldata.Description;

                        }
                        context.InvoiceItems.RemoveRange(context.InvoiceItems.Where(x => x.InvoiceId == invoice.Id));
                        if (modeldata.InvoiceItems.Count > 0)
                        {
                            foreach (var item in modeldata.InvoiceItems)
                            {
                                InvoiceItem invoiceItem2 = new InvoiceItem()
                                {
                                    InvoiceId = modeldata.Id,
                                    GoodsOrServicesId = (int)item.GoodsOrServicesId,
                                    UniqueIdentifier = item.UniqueIdentifier,
                                    Count = (decimal)item.Count,

                                    PriceUnit = (int)item.PriceUnit,
                                    ValueAddedPercentage = Convert.ToInt32(item.ValueAddedPercentage),
                                    Discount = (decimal)item.Discount,
                                };
                                list.Add(invoiceItem2);


                            }
                            await context.InvoiceItems.AddRangeAsync(list);
                            await context.SaveChangesAsync();
                            model.Data = modeldata;
                            model.Status = 201;
                            model.Message = MessageCode.StatusCode.TryGetValue(201, out string messageradd) == true
                                ? model.Message = messageradd
                                : model.Message = "کد وضعیت نامعتبر است.";
                            model.Success = true;

                            return model;
                        }
                    }

                    var conection = ConnectInfo((int)modeldata.CPInfoId);
                    //var isconnect =await IsConnectAsync(conection.TaxMemoryID,conection.PrivateKeyPath, conection.WebServiceAddress);
                    if (await IsConnectApi((int)modeldata.CPInfoId) == true)
                    {

                        if (modeldata.Subject == "اصلی")
                        {
                            var invoice = new Invoice()
                            {
                                CustomerId = IsNoBuyerDisclosureType(SafeInvoiceType(TypePatternToCode(modeldata.TypePattern))) ? null : modeldata.CustomerId,
                                DateOrder = DateOnly.FromDateTime(DateTime.Now),
                                CpInfo = modeldata.CPInfoId,
                                PaymentType = modeldata.PaymentType,
                                Description = modeldata.Description,
                                StatusSentToSystem = modeldata.StatusSentToSystem,
                                ShowButtons = true,
                                Subject = "اصلی"
                            };

                            await context.AddAsync(invoice);
                            await context.SaveChangesAsync();

                            if (modeldata.InvoiceItems.Count > 0)
                            {
                                foreach (var item in modeldata.InvoiceItems)
                                {
                                    InvoiceItem invoiceItem2 = new InvoiceItem()
                                    {
                                        InvoiceId = invoice.Id,
                                        GoodsOrServicesId = (int)item.GoodsOrServicesId,
                                        UniqueIdentifier = item.UniqueIdentifier,
                                        Count = (decimal)item.Count,
                                        PriceUnit = (int)item.PriceUnit,
                                        ValueAddedPercentage = Convert.ToInt32(item.ValueAddedPercentage),
                                        Discount = (decimal)item.Discount,
                                    };
                                    list.Add(invoiceItem2);


                                }
                                await context.InvoiceItems.AddRangeAsync(list);
                                await context.SaveChangesAsync();
                            }

                            //تولید شماره مالیاتی یکتا برای صورتحساب

                            DateTimeOffset now = new DateTimeOffset(DateTime.Now);
                            string taxId = TaxApiService.Instance.TaxIdGenerator.GenerateTaxId(
                            conection.TaxMemoryID,
                            invoice.Id, now.DateTime);
                            string inno = invoice.Id.ToString("X").PadLeft(10, '0');
                            long indatim = now.ToUnixTimeMilliseconds();

                            var invoiceItem = (from ii in context.InvoiceItems
                                               join g in context.GoodsOrServices on ii.GoodsOrServicesId equals g.Id
                                               join um in context.UnitOfMeasurements on g.UnitOfMeasurement equals um.Name
                                               where ii.InvoiceId == invoice.Id
                                               select new
                                               {
                                                   ii.InvoiceId,
                                                   ii.GoodsOrServicesId,
                                                   ii.UniqueIdentifier,
                                                   ii.PriceUnit,
                                                   ii.Count,
                                                   ii.Discount,
                                                   ii.ValueAddedPercentage,
                                                   g.Name,
                                                   g.UnitOfMeasurement,
                                                   um.Code
                                               }).ToList();

                            int pamentType = modeldata.PaymentType == "نقدی" ? 1 : modeldata.PaymentType == "نسیه" ? 2 : 3;

                            var customer = context.Customers.Where(c => c.Id == invoice.CustomerId).FirstOrDefault();
                            var cpInfo = context.CPInfos.Where(c => c.Id == invoice.CpInfo).FirstOrDefault();


                            var bodyItems = invoiceItem.Select(item =>
                            {
                                return new InvoiceBodyDto
                                {
                                    Sstid = item.UniqueIdentifier,//شناسه کالا/خدمت
                                    Sstt = item.Name,
                                    Mu = item.Code,      // کد واحد معتبر
                                    Am = Math.Truncate(item.Count),
                                    Fee = Math.Truncate(item.PriceUnit),
                                    Prdis = Math.Truncate(item.Count * item.PriceUnit),
                                    Dis = Math.Truncate(item.Discount),
                                    Adis = Math.Truncate(item.Count * item.PriceUnit - item.Discount),
                                    Vra = item.ValueAddedPercentage,
                                    Vam = Math.Truncate((item.Count * item.PriceUnit - item.Discount) / 100 * item.ValueAddedPercentage),
                                    Tsstam = Math.Truncate((item.Count * item.PriceUnit - item.Discount) + ((item.Count * item.PriceUnit - item.Discount) / 100 * item.ValueAddedPercentage))
                                };
                            }).ToList();


                            var header = new InvoiceHeaderDto
                            {
                                Taxid = taxId,
                                Inno = inno,
                                Indatim = indatim,
                                Inty = SafeInvoiceType(invoice.TypePattern), // نوع صورتحساب (سرادم): ۱/۲/۳
                                Inp = 1, // الگوی فروش عادی
                                Ins = 1,
                                Tins = cpInfo.EconomicNumber,
                                Tinb = IsNoBuyerDisclosureType(SafeInvoiceType(invoice.TypePattern)) ? null : customer.EconomicNumber,
                                Bid = IsNoBuyerDisclosureType(SafeInvoiceType(invoice.TypePattern)) ? null : customer.NationalID,
                                Tob = IsNoBuyerDisclosureType(SafeInvoiceType(invoice.TypePattern)) ? null : (customer.Type == "حقیقی" ? 1 : 2),
                                Bpc = IsNoBuyerDisclosureType(SafeInvoiceType(invoice.TypePattern)) ? null : customer.PostalCode,
                                Tprdis = bodyItems.Sum(x => x.Prdis),
                                Tdis = bodyItems.Sum(x => x.Dis),
                                Tadis = bodyItems.Sum(x => x.Adis),
                                Tvam = bodyItems.Sum(x => x.Vam),
                                Todam = 0,
                                Tbill = bodyItems.Sum(x => x.Tsstam),
                                Setm = pamentType

                            };

                            InvoiceDto invoice2 = new InvoiceDto
                            {
                                Header = header,
                                Body = bodyItems
                            };


                            List<InvoiceDto> invoiceList = new List<InvoiceDto>();
                            invoiceList.Add(invoice2);


                            var response = TaxApiService.Instance.TaxApis.SendInvoices(
                          invoiceList,
                          null);
                            var packetResponse = response?.Body?.Result?.FirstOrDefault();
                        if (packetResponse is null)
                        {
                            var sendErr = ExtractSdkErrors(response);
                            return new ApiModel
                            {
                                Data = null,
                                Status = 500,
                                Success = false,
                                Message = "ارسال صورتحساب به سامانهٔ مودیان ناموفق بود و آن را «ارسال شده» علامت نزنیم." +
                                    (string.IsNullOrEmpty(sendErr) ? " پاسخی از سامانه برنگشت." : $"\nفهرست خطای برگشتی سامانه: {sendErr}")
                            };
                        }
                            var uid = packetResponse.Uid;
                            var referenceId = packetResponse.ReferenceNumber;
                            //string referenceId = string.Empty;
                            //foreach (var r in responseModels)
                            //    referenceId = r.ReferenceNumber;

                            invoice.ShowButtons = false;

                            var newInvoiceForEdit = context.Invoices.Where(c => c.Id == invoice.Id).FirstOrDefault();
                            newInvoiceForEdit.TaxNumber = taxId;
                            newInvoiceForEdit.StatusSentToSystem = "4";
                            newInvoiceForEdit.ReferenceId = referenceId;

                            context.SaveChanges();
                        }
                        else if (modeldata.Subject == "برگشت از فروش")
                        {
                            Invoice newInvoice = new Invoice()
                            {
                                CustomerId = IsNoBuyerDisclosureType(SafeInvoiceType(TypePatternToCode(modeldata.TypePattern))) ? null : modeldata.CustomerId,
                                //  DateOrder = GetDate(DateTime.Now),
                                DateOrder = DateOnly.FromDateTime(DateTime.Now),
                                PaymentType = modeldata.PaymentType,
                                Description = modeldata.Description,
                                StatusSentToSystem = modeldata.StatusSentToSystem,
                                Subject = "برگشت از فروش",
                                ShowButtons = false
                            };
                            context.Invoices.Add(newInvoice);
                            context.SaveChanges();


                            //تولید شماره مالیاتی یکتا برای صورتحساب
                            DateTimeOffset now = new DateTimeOffset(DateTime.Now);
                            string taxId = TaxApiService.Instance.TaxIdGenerator.GenerateTaxId(
                               conection.TaxMemoryID,
                                newInvoice.Id, now.DateTime);




                            //TaxIdProvider taxIdProvider = new TaxIdProvider(new VerhoeffAlgorithm());
                            //DateTimeOffset now = new DateTimeOffset(DateTime.Now);
                            //string taxId = taxIdProvider.GenerateTaxId(AppInfo.TaxMemoryID, newInvoice.Id, now.DateTime);
                            //string inno = invoiceId.ToString("X").PadLeft(10, '0');
                            //long indatim = now.ToUnixTimeMilliseconds();
                            //تولید شماره مالیاتی یکتا برای صورتحساب


                            string inno = modeldata.Id.ToString("X").PadLeft(10, '0');
                            long indatim = now.ToUnixTimeMilliseconds();

                            var invoiceItem = (from ii in context.InvoiceItems
                                               join g in context.GoodsOrServices on ii.GoodsOrServicesId equals g.Id
                                               join um in context.UnitOfMeasurements on g.UnitOfMeasurement equals um.Name
                                               where ii.InvoiceId == modeldata.Id
                                               select new
                                               {
                                                   ii.InvoiceId,
                                                   ii.GoodsOrServicesId,
                                                   ii.UniqueIdentifier,
                                                   ii.PriceUnit,
                                                   ii.Count,
                                                   ii.Discount,
                                                   ii.ValueAddedPercentage,
                                                   g.Name,
                                                   g.UnitOfMeasurement,
                                                   um.Code
                                               }).ToList();

                            int pamentType = modeldata.PaymentType == "نقدی" ? 1 : modeldata.PaymentType == "نسیه" ? 2 : 3;

                            var customer = context.Customers.Where(c => c.Id == modeldata.CustomerId).FirstOrDefault();
                            var cpInfo = context.CPInfos.Where(c => c.Id == modeldata.CPInfoId).FirstOrDefault();

                            string taxNumber = string.IsNullOrEmpty(modeldata.NewTaxNumber) ? modeldata.TaxNumber : modeldata.NewTaxNumber;

                            var bodyItems = invoiceItem.Select(item =>
                            {
                                return new InvoiceBodyDto
                                {
                                    Sstid = item.UniqueIdentifier,//شناسه کالا/خدمت
                                    Sstt = item.Name,
                                    Mu = item.Code,      // کد واحد معتبر
                                    Am = Math.Truncate(item.Count),
                                    Fee = Math.Truncate(item.PriceUnit),
                                    Prdis = Math.Truncate(item.Count * item.PriceUnit),
                                    Dis = Math.Truncate(item.Discount),
                                    Adis = Math.Truncate(item.Count * item.PriceUnit - item.Discount),
                                    Vra = item.ValueAddedPercentage,
                                    Vam = Math.Truncate((item.Count * item.PriceUnit - item.Discount) / 100 * item.ValueAddedPercentage),
                                    Tsstam = Math.Truncate((item.Count * item.PriceUnit - item.Discount) + ((item.Count * item.PriceUnit - item.Discount) / 100 * item.ValueAddedPercentage))
                                };

                            }).ToList();
                            var header = new InvoiceHeaderDto
                            {
                                Irtaxid = taxNumber,
                                Taxid = taxId,
                                Inno = inno,
                                Indatim = indatim,
                                Inty = SafeInvoiceType(newInvoice.TypePattern), // نوع صورتحساب (سرادم): ۱/۲/۳
                                Inp = 1, // الگوی فروش عادی
                                Ins = 4,
                                Tins = cpInfo.EconomicNumber,
                                Tinb = IsNoBuyerDisclosureType(SafeInvoiceType(newInvoice.TypePattern)) ? null : customer.EconomicNumber,
                                Bid = IsNoBuyerDisclosureType(SafeInvoiceType(newInvoice.TypePattern)) ? null : customer.NationalID,
                                Tob = IsNoBuyerDisclosureType(SafeInvoiceType(newInvoice.TypePattern)) ? null : (customer.Type == "حقیقی" ? 1 : 2),
                                Bpc = IsNoBuyerDisclosureType(SafeInvoiceType(newInvoice.TypePattern)) ? null : customer.PostalCode,
                                Tprdis = bodyItems.Sum(x => x.Prdis),
                                Tdis = bodyItems.Sum(x => x.Dis),
                                Tadis = bodyItems.Sum(x => x.Adis),
                                Tvam = bodyItems.Sum(x => x.Vam),
                                Todam = 0,
                                Tbill = bodyItems.Sum(x => x.Tsstam),
                                Setm = pamentType

                            };

                            InvoiceDto invoice2 = new InvoiceDto
                            {
                                Header = header,
                                Body = bodyItems
                            };
                            List<InvoiceDto> invoiceList = new List<InvoiceDto>();
                            invoiceList.Add(invoice2);
                            var response = TaxApiService.Instance.TaxApis.SendInvoices(
                      invoiceList,
                      null);
                            var packetResponse = response?.Body?.Result?.FirstOrDefault();
                        if (packetResponse is null)
                        {
                            var sendErr = ExtractSdkErrors(response);
                            return new ApiModel
                            {
                                Data = null,
                                Status = 500,
                                Success = false,
                                Message = "ارسال صورتحساب به سامانهٔ مودیان ناموفق بود و آن را «ارسال شده» علامت نزنیم." +
                                    (string.IsNullOrEmpty(sendErr) ? " پاسخی از سامانه برنگشت." : $"\nفهرست خطای برگشتی سامانه: {sendErr}")
                            };
                        }
                            var uid = packetResponse.Uid;
                            var referenceId = packetResponse.ReferenceNumber;

                            ///وضعیت ارسال صورتحساب به مودیان
                            var uidAndFiscalId = new UidAndFiscalId(uid, conection.TaxMemoryID);
                            var inquiryResultModels =
                            TaxApiService.Instance.TaxApis.InquiryByUidAndFiscalId(new(){uidAndFiscalId
                            });


                            modeldata.ShowButtons = false;

                            var newInvoiceForEdit = context.Invoices.Where(c => c.Id == newInvoice.Id).FirstOrDefault();
                            newInvoiceForEdit.NewTaxNumber = taxId;
                            newInvoiceForEdit.TaxNumber = taxNumber;
                            newInvoiceForEdit.ReferenceId = referenceId;

                            context.SaveChanges();
                        }

                        model.Data = modeldata;
                        model.Status = 201;
                        model.Message = MessageCode.StatusCode.TryGetValue(201, out string messager) == true
                            ? model.Message = messager
                            : model.Message = "کد وضعیت نامعتبر است.";
                        model.Success = true;

                        return model;
                    }
                    model.Data = false;
                    model.Status = 500;
                    model.Message = "ارتباط با سرور برقرار نشد";
                    model.Success = false;
                    return model;









                }
                catch (Exception)
                {
                    model.Data = null;
                    model.Status = 500;
                    model.Message = MessageCode.StatusCode.TryGetValue(500, out string messager) == true
                        ? model.Message = messager
                        : model.Message = "کد وضعیت نامعتبر است.";
                    model.Success = false;

                    return model;
                }
            }
        }

        public async Task<ApiModel> IsConnectAsync(string shenaseyekta, string privatekey, string WebServiceAddress)
        {
            ApiModel model = new ApiModel();
            if (string.IsNullOrEmpty(privatekey) || string.IsNullOrEmpty(shenaseyekta))
            {
                return Fail(model, "شناسهٔ حافظه یا مسیر فایل کلید خصوصی ارسال نشده است.");
            }

            static ApiModel Fail(ApiModel m, string msg)
            {
                m.Data = false;
                m.Status = 500;
                m.Message = msg;
                m.Success = false;
                return m;
            }

            // ---------- مرحلهٔ ۱: مقداردهی اولیهٔ SDK با شناسهٔ حافظه و کلید ----------
            try
            {
                TaxApiService.Instance.Init(
                    shenaseyekta,
                    new Pkcs8SignatoryConfig(privatekey, null),
                    new NormalProperties(ClientType.SELF_TSP), // Self Tsp (خود مودی)
                    WebServiceAddress);
            }
            catch (Exception ex)
            {
                return Fail(model,
                    $"تست اتصال ناموفق — مقداردهی اولیهٔ SDK.\n" +
                    $"فهرست خطای سامانه: {ExtractSdkErrorsFromException(ex)}\n" +
                    $"جزئیات فنی: {ExceptionDetails(ex)}\n" +
                    $"بررسی کنید: مسیر فایل کلید درست باشد («{privatekey}») و فرمت کلید PKCS#8 معتبر باشد.");
            }

            // ---------- مرحلهٔ ۲: دریافت اطلاعات سرور (دسترس‌پذیری سامانه + کلیدهای عمومی) ----------
            try
            {
                var serverInfo = TaxApiService.Instance.TaxApis.GetServerInformation();
                if (serverInfo == null)
                {
                    return Fail(model,
                        $"تست اتصال ناموفق — سامانهٔ مودیان در دسترس نیست یا پاسخ معتبری نداد.\n" +
                        $"آدرس سرویس: {WebServiceAddress}\n" +
                        $"شناسهٔ حافظه: {shenaseyekta}\n" +
                        "بررسی کنید: اتصال سرور به اینترنت، حروف بزرگ/کوچک و اسلش انتهای آدرس سرویس (مثل https://tp.tax.gov.ir/req/api/) و عدم‌استفاده از آدرس محیط تست به‌جای آدرس اصلی (یا بالعکس).");
                }
                _ = serverInfo.PublicKeys.FirstOrDefault(); // خواندن کلیدهای عمومی سرور (آماده‌سازی امضا)
            }
            catch (Exception ex)
            {
                return Fail(model,
                    $"تست اتصال ناموفق — خطا در دریافت اطلاعات سرور سامانهٔ مودیان.\n" +
                    $"فهرست خطای سامانه: {ExtractSdkErrorsFromException(ex)}\n" +
                    $"جزئیات فنی: {ExceptionDetails(ex)}\n" +
                    $"آدرس سرویس: {WebServiceAddress}");
            }

            // ---------- مرحلهٔ ۳: دریافت توکن (احراز هویت واقعی مؤدی/خدمات‌دهنده) ----------
            try
            {
                var token = TaxApiService.Instance.TaxApis.RequestToken();
                if (token is null || string.IsNullOrEmpty(token.Token))
                {
                    var sdkErr = ExtractSdkErrors(token);
                    var msg = "تست اتصال ناموفق — احراز هویت در سامانهٔ مودیان انجام نشد و توکنی صادر نشد.";
                    if (!string.IsNullOrEmpty(sdkErr))
                        msg += $"\nفهرست خطای برگشتی سامانه: {sdkErr}";
                    else
                        msg += "\nسامانه خطای متنی برنگرداند.";
                    msg += $"\nشناسهٔ حافظه: {shenaseyekta}\nآدرس سرویس: {WebServiceAddress}\n" +
                           "بررسی کنید: شناسهٔ حافظه دقیقاً مطابق کارپوشهٔ مالیاتی باشد؛ کلید خصوصی با همان شناسهٔ حافظه (CSR آن) صادر شده باشد؛ گواهی در بازهٔ اعتبار زمانی باشد.";
                    return Fail(model, msg);
                }

                model.Data = true;
                model.Status = 201;
                model.Message = "اتصال برقرار و احراز هویت انجام شد — توکن با موفقیت صادر شد.";
                model.Success = true;
                return model;
            }
            catch (Exception ex)
            {
                return Fail(model,
                    $"تست اتصال ناموفق — خطا هنگام دریافت توکن از سامانهٔ مودیان.\n" +
                    $"فهرست خطای سامانه: {ExtractSdkErrorsFromException(ex)}\n" +
                    $"جزئیات فنی: {ExceptionDetails(ex)}\n" +
                    $"شناسهٔ حافظه: {shenaseyekta} | آدرس سرویس: {WebServiceAddress}");
            }

        }

        /// <summary>تست اتصالِ ثبت‌شدهٔ یک شرکت — کلید را از wwwroot/uploads/{نام شرکت} می‌خواند و احراز هویت واقعی انجام می‌دهد.</summary>
        public async Task<ApiModel> IsConnectStoredAsync(int cpinfoId)
        {
            if (await GuardCpInfoAsync(cpinfoId) is { } guard) return guard;
            var connect = await context.ConnectInfos.FirstOrDefaultAsync(c => c.CpInfo == cpinfoId && !c.IsDelete);
            if (connect is null)
                return new ApiModel { Data = false, Status = 400, Success = false, Message = "برای این شرکت اتصالی ثبت نشده است." };
            if (string.IsNullOrEmpty(connect.PrivateKeyPath))
                return new ApiModel { Data = false, Status = 400, Success = false, Message = "کلید خصوصی برای این شرکت ذخیره نشده است." };

            var filePath = await ResolveExistingKeyPathAsync(cpinfoId, connect.PrivateKeyPath);
            if (!System.IO.File.Exists(filePath))
                return new ApiModel { Data = false, Status = 400, Success = false, Message = "فایل کلید خصوصی روی سرور یافت نشد — اتصال را دوباره ذخیره کنید." };

            return await IsConnectAsync(connect.TaxMemoryID, filePath, connect.WebServiceAddress);
        }

        /// <summary>تشخیص سلامت اسکیمای مودیان: وجود هر جدول، ستون‌های درون‌ظرف، تعداد ردیف‌ها — برای مواقعی که لیست‌ها خالی می‌آیند.</summary>
        /// <summary>یک ردیف از گزارش تشخیص اسکیما/دادهٔ مودیان (خروجی Diagnose).</summary>
        private sealed class DiagnoseRow
        {
            public string Table = "";
            public bool Exists;
            public int Columns;
            public List<string> Missing = new();
            public long? TotalRows;
            public long? ActiveRows;
            public string? Linkage;
            public string? Warning;
            public string? Error;
        }

        public async Task<ApiModel> DiagnoseAsync()
        {
            var provider = context.Database.ProviderName ?? "";
            var sqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
            string[] tables = { "CPInfos", "ConnectInfos", "Customers", "UnitOfMeasurements", "GoodsOrServices", "FiscalYears", "Invoices", "InvoiceItems", "InvoicePayments" };
            var expected = new Dictionary<string, string[]>
            {
                ["CPInfos"] = new[] { "CompanyId", "Type", "EnglishName", "PostalCode", "Nationalid", "EconomicNumber", "PersianName", "IsDelete" },
                ["ConnectInfos"] = new[] { "CpInfo", "TaxMemoryID", "PrivateKeyPath", "CertificatePath", "WebServiceAddress", "IsDelete" },
                ["Customers"] = new[] { "CpInfoId", "Name", "IsDelete" },
                ["UnitOfMeasurements"] = new[] { "Name", "Code", "IsDelete" },
                ["GoodsOrServices"] = new[] { "CpInfo", "Name", "UniqueIdentifier", "UnitOfMeasurement", "ValueAddedPercentage", "Price", "IsDelete" },
                ["FiscalYears"] = new[] { "CpInfo", "Title", "StartDate", "EndDate", "CreatedDate", "IsActive", "IsDelete" },
                ["Invoices"] = new[] { "CustomerId", "CpInfo", "FiscalYearId", "TaxMemoryID", "IsDelete" },
                ["InvoiceItems"] = new[] { "InvoiceId", "GoodsOrServicesId", "PriceUnit", "PriceNaghd", "PriceNesye", "IsDelete" },
                ["InvoicePayments"] = new[] { "InvoiceId", "TaxNumber", "Amount", "PaymentDate", "PaymentMethod", "TerminalNumber", "ReferenceNumber", "IsDelete" },
            };
            // جدول‌هایی که به شرکت (CPInfos.Id) لینک‌اند: ستون پیوند برای تشخیص یتیمی/عدم‌نمایش در لیست
            var linkColumn = new Dictionary<string, string>
            {
                ["Customers"] = "CpInfoId",
                ["GoodsOrServices"] = "CpInfo",
                ["FiscalYears"] = "CpInfo",
                ["Invoices"] = "CpInfo",
            };

            var rows = new List<DiagnoseRow>();
            var conn = context.Database.GetDbConnection();
            var openedHere = false;
            try
            {
                if (conn.State != System.Data.ConnectionState.Open) { await conn.OpenAsync(); openedHere = true; }

                static async Task<List<string>> ListColumnsAsync(System.Data.Common.DbConnection c, string table, bool isSqlite)
                {
                    var names = new List<string>();
                    if (isSqlite)
                    {
                        await using var cmd = c.CreateCommand();
                        cmd.CommandText = $"PRAGMA table_info({table})";
                        await using var r = await cmd.ExecuteReaderAsync();
                        while (await r.ReadAsync()) names.Add(r.GetString(1));
                    }
                    else
                    {
                        await using var cmd = c.CreateCommand();
                        cmd.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @t";
                        var p = cmd.CreateParameter();
                        p.ParameterName = "@t";
                        p.Value = table;
                        cmd.Parameters.Add(p);
                        await using var r = await cmd.ExecuteReaderAsync();
                        while (await r.ReadAsync()) names.Add(r.GetString(0));
                    }
                    return names;
                }

                async Task<long?> TryScalarAsync(string sql)
                {
                    try
                    {
                        await using var c = conn.CreateCommand();
                        c.CommandText = sql;
                        var v = await c.ExecuteScalarAsync();
                        return v is null or DBNull ? null : Convert.ToInt64(v);
                    }
                    catch { return null; }
                }

                string Tbl(string t) => sqlite ? t : $"[dbo].[{t}]";

                foreach (var t in tables)
                {
                    var row = new DiagnoseRow { Table = t };
                    await using (var cmd = conn.CreateCommand())
                    {
                        if (sqlite)
                        {
                            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=" + "'" + t + "'";
                        }
                        else
                        {
                            cmd.CommandText = "SELECT CASE WHEN OBJECT_ID(N'dbo." + t + "', N'U') IS NULL THEN 0 ELSE 1 END";
                        }
                        row.Exists = Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0) > 0;
                    }

                    if (row.Exists)
                    {
                        List<string> columns = new();
                        try { columns = await ListColumnsAsync(conn, t, sqlite); }
                        catch (Exception colEx) { row.Error = colEx.Message; }
                        row.Columns = columns.Count;
                        row.Missing = expected.ContainsKey(t)
                            ? expected[t].Where(c => !columns.Any(x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase))).ToList()
                            : new List<string>();

                        var total = await TryScalarAsync($"SELECT COUNT(*) FROM {Tbl(t)}");
                        row.TotalRows = total;

                        bool hasIsDelete = columns.Any(x => string.Equals(x, "IsDelete", StringComparison.OrdinalIgnoreCase));
                        if (hasIsDelete)
                            row.ActiveRows = await TryScalarAsync($"SELECT COUNT(*) FROM {Tbl(t)} WHERE IsDelete = 0");

                        // نمونهٔ کلید/پیوند (۱۰ ردیف اول) — برای دیدنِ اینکه ردیف‌ها به کدام شرکت وصل‌اند
                        if (linkColumn.TryGetValue(t, out var linkCol) && columns.Any(x => string.Equals(x, linkCol, StringComparison.OrdinalIgnoreCase)))
                        {
                            try
                            {
                                await using var c = conn.CreateCommand();
                                c.CommandText = sqlite
                                    ? $"SELECT Id, {linkCol} FROM {t} ORDER BY Id LIMIT 10"
                                    : $"SELECT TOP 10 Id, {linkCol} FROM {Tbl(t)} ORDER BY Id";
                                var samples = new List<string>();
                                await using var r = await c.ExecuteReaderAsync();
                                while (await r.ReadAsync())
                                {
                                    var link = r.IsDBNull(1) ? "—" : Convert.ToString(r.GetValue(1));
                                    samples.Add($"{Convert.ToString(r.GetValue(0))}→{link}");
                                }
                                row.Linkage = string.Join("، ", samples);
                            }
                            catch { /* نمونه‌گیری اختیاری است */ }
                        }
                        else if (t == "CPInfos")
                        {
                            try
                            {
                                await using var c = conn.CreateCommand();
                                c.CommandText = sqlite
                                    ? $"SELECT Id, PersianName, IsDelete FROM {t} ORDER BY Id LIMIT 10"
                                    : $"SELECT TOP 10 Id, PersianName, IsDelete FROM {Tbl(t)} ORDER BY Id";
                                var samples = new List<string>();
                                await using var r = await c.ExecuteReaderAsync();
                                while (await r.ReadAsync())
                                {
                                    var del = !r.IsDBNull(2) && Convert.ToInt64(r.GetValue(2)) != 0 ? " (حذف‌شده)" : "";
                                    samples.Add($"{Convert.ToString(r.GetValue(0))}·{Convert.ToString(r.GetValue(1))}{del}");
                                }
                                row.Linkage = string.Join("، ", samples);
                            }
                            catch { /* نمونه‌گیری اختیاری است */ }
                        }
                    }
                    rows.Add(row);
                }

                // ---------- هشدارهای داده‌ای: علت رایج «ذخیره می‌شود ولی تو لیست نیست» ----------
                bool TableExistsHere(string t) => rows.Any(r => r.Table == t && r.Exists);
                if (TableExistsHere("CPInfos"))
                {
                    var activeCp = await TryScalarAsync($"SELECT COUNT(*) FROM {Tbl("CPInfos")} WHERE IsDelete = 0");
                    foreach (var (t, linkCol) in linkColumn)
                    {
                        if (!TableExistsHere(t)) continue;
                        var row = rows.First(r => r.Table == t);
                        long? orphans = null;
                        try
                        {
                            await using var c = conn.CreateCommand();
                            c.CommandText =
                                $"SELECT COUNT(*) FROM {Tbl(t)} x WHERE x.IsDelete = 0 AND " +
                                $"(x.{linkCol} IS NULL OR NOT EXISTS (SELECT 1 FROM {Tbl("CPInfos")} p WHERE p.Id = x.{linkCol} AND p.IsDelete = 0))";
                            orphans = Convert.ToInt64(await c.ExecuteScalarAsync() ?? 0);
                        }
                        catch { /* ستون ممکن است قدیمی باشد */ }
                        if (orphans > 0)
                            row.Warning = $"⚠ {orphans} ردیف فعال به شرکتِ ناموجود یا حذف‌شده لینک است — با شرکت انتخابی فعلی در لیست دیده نمی‌شود.";
                        else if (row.TotalRows > 0 && row.ActiveRows == 0)
                            row.Warning = "⚠ همهٔ ردیف‌ها حذف‌شده (IsDelete=1) هستند — در لیست دیده نمی‌شوند.";
                    }
                    var cpRow = rows.First(r => r.Table == "CPInfos");
                    if (cpRow.TotalRows > 0 && cpRow.ActiveRows == 0)
                        cpRow.Warning = "⚠ همهٔ شرکت‌ها حذف‌شده‌اند (IsDelete=1) — نوار انتخاب شرکت خالی می‌ماند.";
                    if ((activeCp ?? 0) == 0 && cpRow.TotalRows > 0 && cpRow.Warning is null)
                        cpRow.Warning = "⚠ شرکتی فعال نیست.";
                }

                foreach (var row in rows)
                    row.Warning ??= (row.TotalRows > 0 && row.ActiveRows == 0 && row.Exists
                        ? "⚠ همهٔ ردیف‌ها حذف‌شده (IsDelete=1) هستند — در لیست دیده نمی‌شوند."
                        : null);
            }
            catch (Exception ex)
            {
                return new ApiModel { Status = 500, Success = false, Message = "تشخیص اسکیما ناموفق: " + ex.Message };
            }
            finally
            {
                if (openedHere) await conn.CloseAsync();
            }

            var report = rows.Select(r => new
            {
                table = r.Table,
                exists = r.Exists,
                columns = r.Columns,
                missing = r.Missing,
                totalRows = r.TotalRows,
                activeRows = r.ActiveRows,
                linkage = r.Linkage,
                warning = r.Warning,
                error = r.Error
            }).ToList<object>();

            return new ApiModel
            {
                Data = new { provider, tables = report },
                Status = 200,
                Success = true,
                Message = MessageCode.StatusCode.TryGetValue(200, out string m) ? m : "کد وضعیت نامعتبر است."
            };
        }

        /// <summary>تشخیص عمیق: دیتابیسِ واقعیِ متصل + شناسهٔ شرکتِ ارسالی کلاینت + نتیجهٔ دقیقِ همان کوئری‌های لیست با EF — برای «ردیف هست ولی لیست خالی است».</summary>
        public async Task<ApiModel> DiagnoseListsAsync(int cpinfoId)
        {
            if (await GuardCpInfoAsync(cpinfoId) is { } guard) return guard;
            try
            {
                var conn = context.Database.GetDbConnection();
                var activeCpIds = await context.CPInfos.Where(c => !c.IsDelete).OrderBy(c => c.Id).Select(c => c.Id).ToListAsync();
                var allCp = await context.CPInfos.OrderBy(c => c.Id).Select(c => new { c.Id, c.IsDelete, c.PersianName }).ToListAsync();

                object units;
                try
                {
                    var items = await context.UnitOfMeasurements.OrderBy(x => x.Id)
                        .Select(x => new { x.Id, x.Name, x.Code, x.IsDelete }).ToListAsync();
                    units = new
                    {
                        efCountSameQueryAsList = items.Count,
                        rows = items.Select(x => $"{x.Id}·{x.Name}{(x.IsDelete ? "(حذف)" : "")}").ToList()
                    };
                }
                catch (Exception ex) { units = new { error = ex.GetType().Name + ": " + ex.Message }; }

                object goods;
                try
                {
                    var rows = await context.GoodsOrServices.OrderBy(x => x.Id)
                        .Select(x => new { x.Id, x.CpInfo, x.IsDelete, x.Name }).ToListAsync();
                    goods = new
                    {
                        efCountSameFilterAsList = rows.Count(x => x.CpInfo == cpinfoId && !x.IsDelete),
                        rows = rows.Select(x => $"{x.Id}→cp{x.CpInfo}{(x.IsDelete ? "(حذف)" : "")} {x.Name}").ToList()
                    };
                }
                catch (Exception ex) { goods = new { error = ex.GetType().Name + ": " + ex.Message }; }

                object customers;
                try
                {
                    var rows = await context.Customers.OrderBy(x => x.Id)
                        .Select(x => new { x.Id, x.CpInfoId, x.IsDelete, x.Name }).ToListAsync();
                    customers = new
                    {
                        efCountSameFilterAsList = rows.Count(x => x.CpInfoId == cpinfoId && !x.IsDelete),
                        rows = rows.Select(x => $"{x.Id}→cp{(x.CpInfoId.HasValue ? x.CpInfoId.ToString() : "null")}{(x.IsDelete ? "(حذف)" : "")} {x.Name}").ToList()
                    };
                }
                catch (Exception ex) { customers = new { error = ex.GetType().Name + ": " + ex.Message }; }

                object invoices;
                try
                {
                    var rows = await context.Invoices.OrderBy(x => x.Id)
                        .Select(x => new { x.Id, x.CpInfo, x.IsDelete, x.StatusSentToSystem }).ToListAsync();
                    var svc = await GetAllInvoiceAsync("همه", cpinfoId, new GetResultQuery { Page = 1, PageSize = 10 });
                    string? svcTotal = null;
                    if (svc.Data is InvoiceResponse ir && ir.Invoices is not null)
                        svcTotal = ir.Invoices.TotalCount.ToString();
                    invoices = new
                    {
                        efCountSameFilterAsList = rows.Count(x => x.CpInfo == cpinfoId && !x.IsDelete),
                        rows = rows.Select(x => $"{x.Id}→cp{(x.CpInfo.HasValue ? x.CpInfo.ToString() : "null")}{(x.IsDelete ? "(حذف)" : "")} st={x.StatusSentToSystem}").ToList(),
                        listServiceCall = new { svc.Success, svc.Status, svc.Message, totalCount = svcTotal }
                    };
                }
                catch (Exception ex) { invoices = new { error = ex.GetType().Name + ": " + ex.Message }; }

                var result = new
                {
                    db = new { provider = context.Database.ProviderName, dataSource = conn.DataSource, database = conn.Database },
                    cpinfoIdFromClient = cpinfoId,
                    activeCpInfoIds = activeCpIds,
                    cpInfoTable = allCp.Select(c => $"{c.Id}·{c.PersianName}{(c.IsDelete ? "(حذف)" : "")}").ToList(),
                    units,
                    goods,
                    customers,
                    invoices
                };

                return new ApiModel
                {
                    Data = result,
                    Status = 200,
                    Success = true,
                    Message = MessageCode.StatusCode.TryGetValue(200, out string m) ? m : "کد وضعیت نامعتبر است."
                };
            }
            catch (Exception ex)
            {
                return new ApiModel { Status = 500, Success = false, Message = "تشخیص عمیق ناموفق: " + ex.Message };
            }
        }

        /// <summary>حذف نرم شرکت (و اتصال‌هایش) — صورتحساب‌ها و سوابق حفظ می‌شوند.</summary>
        public async Task<ApiModel> DeleteCpInfoAsync(int id)
        {
            if (await GuardCpInfoAsync(id) is { } guard) return guard;
            var cp = await context.CPInfos.FindAsync(id);
            if (cp is null)
                return new ApiModel { Data = null, Status = 404, Success = false, Message = "شرکت مورد نظر یافت نشد." };

            cp.IsDelete = true;
            var connections = context.ConnectInfos.Where(x => x.CpInfo == id && !x.IsDelete).ToList();
            foreach (var c in connections) c.IsDelete = true;
            await context.SaveChangesAsync();

            return new ApiModel { Data = null, Status = 200, Success = true, Message = "شرکت حذف شد." };
        }

        public async Task<ApiModel> GetAllInvoiceAsync(string operation, int CpinfoId, GetResultQuery request)
        {
            if (await GuardCpInfoAsync(CpinfoId) is { } guard) return guard;
            ApiModel model = new ApiModel();

            try
            {

                var query = context.Invoices.Where(c => c.CpInfo == CpinfoId && !c.IsDelete).AsQueryable();



                // جستجو صورتحساب بر اساس موضوع، شماره مالیاتی جدید و مرجع و وضعیت  
                if (!string.IsNullOrEmpty(request.SearchTerm))
                {
                    var customer = from i in context.Invoices
                                   join c in context.Customers
                                       on i.CustomerId equals c.Id
                                   select new
                                   {
                                       Invoice = i,
                                       CustomerName = c.Name
                                   };





                    query = query.Where(c =>
     c.Subject.Trim().Contains(request.SearchTerm) ||
     c.InvoiceNumber.ToString().Contains(request.SearchTerm) ||
     c.TaxNumber.Trim().Contains(request.SearchTerm) ||
     c.NewTaxNumber.Trim().Contains(request.SearchTerm) ||

     // ✅ جستجو در وضعیت (تبدیل کد به متن)
     (c.StatusSentToSystem == "0" && "ارسال نشده".Contains(request.SearchTerm)) ||
     (c.StatusSentToSystem == "1" && "ارسال شده در انتظار استعلام".Contains(request.SearchTerm)) ||
     (c.StatusSentToSystem == "2" && "در انتظار بررسی کارپوشه".Contains(request.SearchTerm)) ||
     (c.StatusSentToSystem == "3" && "ارسال شده".Contains(request.SearchTerm)) ||
     (c.StatusSentToSystem == "4" && "ارسال شده در انتظار واکنش".Contains(request.SearchTerm)) ||
     (c.StatusSentToSystem == "5" && "دارای خطا".Contains(request.SearchTerm)) ||
     (c.StatusSentToSystem == "6" && "بدون نیاز به واکنش".Contains(request.SearchTerm)) ||
     (c.StatusSentToSystem == "7" && "تائید شده".Contains(request.SearchTerm)) ||
     (c.StatusSentToSystem == "8" && "رد شده".Contains(request.SearchTerm))
     ||
     (c.TypePattern == 1 && "نوع اول".Contains(request.SearchTerm)) ||
        (c.TypePattern == 2 && "نوع دوم".Contains(request.SearchTerm)) ||
        (c.TypePattern == 3 && "نوع سوم".Contains(request.SearchTerm)) ||

context.Customers.Any(x =>
    x.Id == c.CustomerId &&
    x.Name.Contains(request.SearchTerm))

 );
                    // query = query.Where(c => c.Subject.Trim().Contains(request.SearchTerm) || c.TaxNumber.Trim().Contains(request.SearchTerm)|| c.NewTaxNumber.Trim().Contains(request.SearchTerm) || c.StatusSentToSystem.Trim().Contains(request.SearchTerm) );
                }
                if (request.FirstDate != null || request.EndDate != null)
                {
                    var firstDateOnly = DateOnly.FromDateTime(request.FirstDate.Value);
                    var endDateOnly = DateOnly.FromDateTime(request.EndDate.Value);
                    // صورتحساب را از تاریخ فلان تا تاریخ فلان نمایش دهد
                    if (request.FirstDate != null && request.EndDate != null)
                    {


                        query = query.Where(c =>
                            c.DateOrder >= firstDateOnly && c.DateOrder <= endDateOnly);
                    }
                    //صورتحساب را از تاریخ فلان به بعد
                    else if (request.FirstDate != null)
                    {
                        query = query.Where(c =>
                       c.DateOrder >= firstDateOnly);
                    }
                    //صورتحساب را از تاریخ فلان به قبل
                    else if (request.EndDate != null)
                    {
                        query = query.Where(c =>
                       c.DateOrder <= endDateOnly);
                    }
                }


                //Order => صعودی یا نزولی desc/asc
                //بعد از آن مرتب کن بر اساس ستونی که کاربر می دهد SortColumn
                if (request.SortColumn != null)
                {
                    if (request.SortOrder?.ToLower() == "desc")
                    {
                        query = request.SortColumn.ToLower() switch
                        {

                            "TaxNumber" => query.OrderByDescending(x => x.TaxNumber),


                            _ => query.OrderByDescending(x => x.Id)
                        };
                    }
                    else
                    {
                        query = request.SortColumn.ToLower() switch
                        {

                            "TaxNumber" => query.OrderBy(x => x.TaxNumber),


                            _ => query.OrderBy(x => x.Id)
                        };
                    }
                }
                else
                {
                    query = query.OrderByDescending(x => x.Id);
                }
                UCInvoiceInfo UcinvoiceInfo = new UCInvoiceInfo();

                var invoices = operation == "همه"
                 ? query
                 : query.Where(x => x.StatusSentToSystem == operation);

                IQueryable<InvoiceVM> queryableInvoices;
                InvoiceVM invoiceLabels = new InvoiceVM();

                // ✅ محاسبه لیبل‌ها قبل از صفحه‌بندی و روی کل داده‌ها
                invoiceLabels.lblAll = await query.CountAsync();
                invoiceLabels.lblSentToSystem = await query.CountAsync(x => x.StatusSentToSystem == "4");
                invoiceLabels.lblInquiry = await query.CountAsync(x => x.StatusSentToSystem == "7");
                invoiceLabels.lblLackOfResponsiveness = await query.CountAsync(x => x.StatusSentToSystem == "2");
                invoiceLabels.lblError = await query.CountAsync(x => x.StatusSentToSystem == "5");
                invoiceLabels.lblNotSentToSystem = await query.CountAsync(x => x.StatusSentToSystem == "0");

                queryableInvoices = invoices.Select(i => new InvoiceVM
                {
                    Id = i.Id,
                    Name = i.Id.ToString(),
                    DateOrder = i.DateOrder,
                    PaymentType = i.PaymentType,
                    NumberInvoice = i.InvoiceNumber,
                    Subject = i.Subject,
                    ShowButtons = i.ShowButtons,
                    CPInfoId = i.CpInfo,
                    StatusSentToSystemInt = i.StatusSentToSystem,
                    TypePattern = i.TypePattern == 1 ? "نوع اول" : i.TypePattern == 2 ? "نوع دوم" : "نوع سوم",
                    StatusSentToSystem = i.StatusSentToSystem == "0" ? "ارسال نشده" :
                        i.StatusSentToSystem == "1" ? "ارسال شده در انتظار استعلام" :
                        i.StatusSentToSystem == "2" ? "در انتظار بررسی کارپوشه" :
                        i.StatusSentToSystem == "3" ? "ارسال شده" :
                        i.StatusSentToSystem == "4" ? "ارسال شده در انتظار واکنش" :
                        i.StatusSentToSystem == "5" ? "دارای خطا" :
                        i.StatusSentToSystem == "6" ? "بدون نیاز به واکنش" :
                        i.StatusSentToSystem == "7" ? "تأیید شده" :
                        i.StatusSentToSystem == "8" ? "رد شده" : "وضعیت نامشخص",
                    TaxNumber = i.TaxNumber,
                    NewTaxNumber = i.NewTaxNumber,
                    ReferenceId = i.ReferenceId,
                    CustomerName = context.Customers.Where(c => c.Id == i.CustomerId).Select(c => c.Name).FirstOrDefault(),
                    CustomerId = i.CustomerId,

                    TotallMoney = context.InvoiceItems
        .Where(x => x.InvoiceId == i.Id)
        .Sum(x => (x.PriceUnit * x.Count - x.Discount) +
                 ((x.PriceUnit * x.Count - x.Discount) * x.ValueAddedPercentage / 100))
                }).AsQueryable();



                var data = await PagedList<InvoiceVM>.CreateAsync(queryableInvoices, (int)request.Page, (int)request.PageSize);
                model.Data = new InvoiceResponse
                {
                    Invoices = data,
                    Labels = invoiceLabels
                };

                model.Status = 200;
                model.Success = true;
                model.Message = MessageCode.StatusCode.TryGetValue(200, out string messager) == true
                    ? model.Message = messager
                    : model.Message = "کد وضعیت نامعتبر است.";
                return model;
            }
            catch (Exception exception)
            {
                model.Data = null;
                model.Message = MessageCode.StatusCode.TryGetValue(500, out string messager) == true
                    ? model.Message = messager
                    : model.Message = exception.Message;
                model.Status = 500;
                model.Success = false;
                return model;
            }




        }
        private async Task<bool> IsConnectApi(int CpinfoId)
        {
            var conection = ConnectInfo(CpinfoId);
            if (conection is null || string.IsNullOrEmpty(conection.PrivateKeyPath))
                return false;

            // مسیر کلید — اول wwwroot/uploads/{نام شرکت}، سپس مسیر قدیمی File/Moadian
            string filePath = await ResolveExistingKeyPathAsync(CpinfoId, conection.PrivateKeyPath);
            if (!System.IO.File.Exists(filePath))
                return false;

            // Init + احراز هویت واقعی (با جزئیات مرحله‌ای خطا)
            var isconnect = await IsConnectAsync(conection.TaxMemoryID, filePath, conection.WebServiceAddress);
            if (isconnect.Success)
            {
                return true;
            }

            return false;
        }
        public async Task<ApiModel> SendInvoiceAsync(VmInvoiceItems.InvoiceVM modelInvoiceVM, bool previewOnly = false)
        {
            if (await GuardCpInfoAsync(modelInvoiceVM.CPInfoId ?? 0) is { } guard) return guard;
            ApiModel model = new ApiModel();
            string? requestJsonSnapshot = null;
            string? responseJsonSnapshot = null;
            try
            {
                // اعتبارسنجی ورودی قبل از cast/اتصال: DTO ناقص نباید با Id=0 وارد فلو شود.
                if (modelInvoiceVM is null || modelInvoiceVM.Id <= 0)
                    return new ApiModel { Data = null, Status = 400, Success = false,
                        Message = "شناسهٔ صورتحساب نامعتبر است؛ جزئیات صورتحساب را دوباره بارگذاری و ارسال کنید." };

                var invoiceForEdit = await context.Invoices.FirstOrDefaultAsync(c => c.Id == modelInvoiceVM.Id);
                if (invoiceForEdit is null)
                    return new ApiModel { Data = null, Status = 404, Success = false,
                        Message = "صورتحساب برای ارسال یافت نشد." };

                // شرکت مرجع از رکورد ذخیره‌شده گرفته می‌شود؛ CPInfoId ارسالی کلاینت قابل اتکا نیست.
                if (!invoiceForEdit.CpInfo.HasValue || invoiceForEdit.CpInfo.Value <= 0)
                    return new ApiModel { Data = null, Status = 400, Success = false,
                        Message = "شرکت مرتبط با این صورتحساب مشخص نیست." };
                var cpInfoId = invoiceForEdit.CpInfo.Value;
                var conection = ConnectInfo(cpInfoId);
                // پیش‌نمایش فقط از داده‌های محلی ساخته می‌شود و هیچ اتصال/درخواستی به سامانه نمی‌زند.
                if (conection is null || (!previewOnly && !await IsConnectApi(cpInfoId)))
                    return new ApiModel { Data = null, Status = 502, Success = false,
                        Message = "اتصال معتبر به سامانهٔ مودیان یافت نشد. ابتدا اطلاعات اتصال شرکت را بررسی کنید." };

                // Subject در رکورد معمولاً «اصلی» است؛ Subject درخواست، نوع عملیات را مشخص می‌کند.
                // برای سازگاری با کلاینت‌های قدیمی، درخواست خالی/«اصلی» هم ارسال عادی محسوب می‌شود.
                var operationSubject = modelInvoiceVM.Subject?.Trim();
                var isSalesReturn = operationSubject == "برگشت از فروش";
                var isNormalSend = operationSubject == "ارسال به سامانه"
                    || string.IsNullOrEmpty(operationSubject) || operationSubject == "اصلی";
                if (!isNormalSend && !isSalesReturn)
                    return new ApiModel { Data = null, Status = 400, Success = false,
                        Message = $"نوع عملیات ارسال معتبر نیست: «{operationSubject}»." };
                if (previewOnly && !isNormalSend)
                    return new ApiModel { Data = null, Status = 400, Success = false,
                        Message = "پیش‌نمایش JSON فقط برای صورتحساب اصلی/ارسال عادی فعال است." };
                if (isNormalSend && invoiceForEdit.StatusSentToSystem is not ("0" or "5"))
                    return new ApiModel { Data = null, Status = 409, Success = false,
                        Message = "فقط صورتحسابِ ارسال‌نشده یا دارای خطای قطعی قابل ارسال مجدد است." };

                // TaxID و Indatim باید دقیقاً از یک لحظه ساخته شوند؛ UTC از اختلاف timezone/Kind
                // بین سرور و SDK جلوگیری می‌کند و سریال Inno نیز باید با ورودی Generator یکی باشد.
                var issueTime = DateTimeOffset.UtcNow;
                var invoiceSerial = invoiceForEdit.Id;
                var fiscalId = conection.TaxMemoryID?.Trim();
                if (string.IsNullOrWhiteSpace(fiscalId))
                    return new ApiModel { Data = null, Status = 400, Success = false,
                        Message = "شناسهٔ یکتای حافظهٔ مالیاتی برای شرکت تنظیم نشده است." };
                string taxId = TaxApiService.Instance.TaxIdGenerator.GenerateTaxId(
                    fiscalId, invoiceSerial, issueTime.UtcDateTime);
                string inno = invoiceSerial.ToString("X").PadLeft(10, '0');
                long indatim = issueTime.ToUnixTimeMilliseconds(); // همان لحظهٔ استفاده‌شده برای TaxID
                if (isNormalSend)
                    {

                        try
                        {
                            var invoiceItem = (from ii in context.InvoiceItems
                                               join g in context.GoodsOrServices on ii.GoodsOrServicesId equals g.Id
                                               join um in context.UnitOfMeasurements on g.UnitOfMeasurement equals um.Name
                                               where ii.InvoiceId == modelInvoiceVM.Id
                                               select new
                                               {
                                                   ii.InvoiceId,
                                                   ii.GoodsOrServicesId,
                                                   ii.UniqueIdentifier,
                                                   ii.PriceUnit,
                                                   ii.Count,
                                                   ii.Discount,
                                                   ii.GoodsOrServicesDescripton,
                                                   ii.ValueAddedPercentage,
                                                   g.Name,
                                                   //ii.PriceNaghd,
                                                   //ii.PriceNesye,
                                                   g.UnitOfMeasurement,
                                                   um.Code
                                               }).ToList();

                            int pamentType = invoiceForEdit.PaymentType switch
                            {
                                "نقدی" => 1,
                                "نسیه" => 2,
                                _ => 0
                            };
                            if (pamentType == 0)
                                return new ApiModel { Data = null, Status = 400, Success = false,
                                    Message = "نوع پرداخت صورتحساب نامعتبر است؛ فقط «نقدی» یا «نسیه» قابل ارسال است." };

                            var typeSettleErr = ValidateSettlementForType(invoiceForEdit.TypePattern, pamentType);

                            if (typeSettleErr is not null)

                                return new ApiModel { Data = null, Status = 400, Success = false, Message = typeSettleErr };


                            var customer = context.Customers.Where(c => c.Id == invoiceForEdit.CustomerId).FirstOrDefault();
                            var cpInfo = context.CPInfos.Where(c => c.Id == invoiceForEdit.CpInfo).FirstOrDefault();
                            ////
                            //foreach (var c in invoiceItem)
                            //{
                            //    Console.WriteLine(c.UniqueIdentifier);
                            //    Console.WriteLine(c.UniqueIdentifier.Length);

                            //    foreach (var d in c.UniqueIdentifier)
                            //    {
                            //        Console.WriteLine($"{d} -> {(int)d}");
                            //    }

                            //}


                            var bodyItems = invoiceItem.Select(item =>
                            { 

                                return new InvoiceBodyDto
                                {
                                    Sstid = item.UniqueIdentifier.Trim(),//شناسه کالا/خدمت
                                   // Sstt = item.Name,
                                    Sstt = item.Name + (item.GoodsOrServicesDescripton != null ? " " + item.GoodsOrServicesDescripton : ""),
                                    Mu = item.Code,      // کد واحد معتبر
                                    //Am =  item.Count,
                                    Am = Math.Truncate(item.Count),
                                    //Cop = item.PriceNaghd,//مبلغ نقدی

                                    Fee = Math.Truncate(item.PriceUnit),

                                    Prdis = Math.Truncate(item.Count * item.PriceUnit),

                                    Dis = Math.Truncate(item.Discount),

                                    Adis = Math.Truncate(item.Count * item.PriceUnit - item.Discount),
                                    Vra = item.ValueAddedPercentage,

                                    Vam = Math.Truncate((item.Count * item.PriceUnit - item.Discount) / 100 * item.ValueAddedPercentage),

                                    Tsstam = Math.Truncate((item.Count * item.PriceUnit - item.Discount) + ((item.Count * item.PriceUnit - item.Discount) / 100 * item.ValueAddedPercentage)),
                                };

                            }).ToList();

                            var header = new InvoiceHeaderDto
                            {
                                Taxid = taxId,
                                Inno = inno,
                                Indatim = indatim,
                                Indati2m = indatim,
                                Inty = SafeInvoiceType(invoiceForEdit.TypePattern), // نوع صورتحساب (سرادم): ۱/۲/۳
                                Inp = 1, // الگوی فروش عادی
                                Ins = 1,///invoiceSubject
                                Tins = cpInfo.EconomicNumber,
                                Tinb = IsNoBuyerDisclosureType(SafeInvoiceType(invoiceForEdit.TypePattern)) ? null : customer.EconomicNumber,
                                Bid = IsNoBuyerDisclosureType(SafeInvoiceType(invoiceForEdit.TypePattern)) ? null : customer.NationalID,
                                Tob = IsNoBuyerDisclosureType(SafeInvoiceType(invoiceForEdit.TypePattern)) ? null : (customer.Type == "حقیقی" ? 1 : 2),
                                Bpc = IsNoBuyerDisclosureType(SafeInvoiceType(invoiceForEdit.TypePattern)) ? null : customer.PostalCode,
                                Tprdis = bodyItems.Sum(x => x.Prdis),
                                Tdis = bodyItems.Sum(x => x.Dis),
                                Tadis = bodyItems.Sum(x => x.Adis),
                                Tvam = bodyItems.Sum(x => x.Vam),
                                Todam = 0,
                                Tbill = bodyItems.Sum(x => x.Tsstam),///کل صورتحساب
                                Setm = pamentType, ///نوع پرداخت
                                // PDF SDK این‌ها را سهم نقدی/نسیهٔ سربرگ تعریف می‌کند؛ فرم همین مقادیر را ذخیره می‌کند.
                                Cap = IsNoBuyerDisclosureType(SafeInvoiceType(invoiceForEdit.TypePattern)) ? bodyItems.Sum(x => x.Tsstam) : invoiceForEdit.PriceNaghd,
                                Insp = IsNoBuyerDisclosureType(SafeInvoiceType(invoiceForEdit.TypePattern)) ? 0 : invoiceForEdit.PriceNesye,


                            };

                            InvoiceDto invoice2 = new InvoiceDto
                            {
                                Header = header,
                                Body = bodyItems
                            };


                            List<InvoiceDto> invoiceList = new List<InvoiceDto>();
                            invoiceList.Add(invoice2);

                            // Snapshot از DTO؛ پیش‌نمایش مسیر ارسال SDK/شبکه را اجرا نمی‌کند.
                            requestJsonSnapshot = JsonConvert.SerializeObject(invoiceList, Formatting.Indented);
                            if (previewOnly)
                            {
                                return new ApiModel
                                {
                                    Data = null,
                                    Status = 200,
                                    Success = true,
                                    Message = "پیش‌نمایش ساخته شد؛ هیچ صورتحسابی ارسال نشده است.",
                                    RequestJson = requestJsonSnapshot
                                };
                            }

                            var response = TaxApiService.Instance.TaxApis.SendInvoices(
                                invoiceList,
                                null);
                            responseJsonSnapshot = JsonConvert.SerializeObject(response?.Body, Formatting.Indented);
                            var packetResponse = response?.Body?.Result?.FirstOrDefault();
                        if (packetResponse is null)
                        {
                            var sendErr = ExtractSdkErrors(response);
                            return new ApiModel
                            {
                                Data = null,
                                Status = 500,
                                Success = false,
                                Message = "ارسال صورتحساب به سامانهٔ مودیان ناموفق بود و آن را «ارسال شده» علامت نزنیم." +
                                    (string.IsNullOrEmpty(sendErr) ? " پاسخی از سامانه برنگشت." : $"\nفهرست خطای برگشتی سامانه: {sendErr}"),
                                RequestJson = requestJsonSnapshot,
                                ResponseJson = responseJsonSnapshot
                            };
                        }
                            var uid = packetResponse.Uid;
                            var referenceId = packetResponse.ReferenceNumber;
                            //string referenceId = string.Empty;
                            //foreach (var r in responseModels)
                            //    referenceId = r.ReferenceNumber;

                            invoiceForEdit.ShowButtons = false;

                            var newInvoiceForEdit = context.Invoices.Where(c => c.Id == modelInvoiceVM.Id).FirstOrDefault();
                            newInvoiceForEdit.TaxNumber = taxId;
                            newInvoiceForEdit.StatusSentToSystem = "1";
                            newInvoiceForEdit.ReferenceId = referenceId;

                            context.SaveChanges();
                            model.Data = modelInvoiceVM;
                            model.RequestJson = requestJsonSnapshot;
                            model.ResponseJson = responseJsonSnapshot;
                            model.Status = 201;
                            model.Success = true;
                            model.Message = MessageCode.StatusCode.TryGetValue(201, out string messagerr) == true
                                ? model.Message = messagerr
                                : model.Message = "کد وضعیت نامعتبر است.";
                            return model;
                        }
                        catch (Exception ex)
                        {

                            model.Data = null;
                            model.RequestJson = requestJsonSnapshot;
                            model.ResponseJson = responseJsonSnapshot;
                            model.Message = MessageCode.StatusCode.TryGetValue(500, out string messagerss) == true
                                ? model.Message = messagerss
                                : model.Message = ex.Message;
                            model.Status = 500;
                            model.Success = false;
                            return model;
                        }

                    }
                    else if (isSalesReturn)
                    {
                        var invoice = new Invoice()
                        {
                            CustomerId = invoiceForEdit.CustomerId,
                            DateOrder = DateOnly.FromDateTime(DateTime.Now),
                            PaymentType = invoiceForEdit.PaymentType,
                            Description = invoiceForEdit.Description,
                            CpInfo = invoiceForEdit.CpInfo,
                            /// StatusSentToSystem = modeldata.StatusSentToSystem,
                            StatusSentToSystem = "6",//عدم نیاز به واکنش
                            ShowButtons = false,
                            Subject = "ابطالی"
                        };
                        invoice.FiscalYearId = invoiceForEdit.FiscalYearId;
                        await context.AddAsync(invoice);
                        await context.SaveChangesAsync();
                        //تولید شماره مالیاتی یکتا برای صورتحساب

                        var returnIssueTime = DateTimeOffset.UtcNow;
                        string taxId1 = TaxApiService.Instance.TaxIdGenerator.GenerateTaxId(
                            fiscalId, invoice.Id, returnIssueTime.UtcDateTime);
                        // TaxID و Inno باید از یک سریال داخلی ساخته شوند.
                        string inno1 = invoice.Id.ToString("X").PadLeft(10, '0');
                        long indatim1 = returnIssueTime.ToUnixTimeMilliseconds();



                        var invoiceItem = (from ii in context.InvoiceItems
                                           join g in context.GoodsOrServices on ii.GoodsOrServicesId equals g.Id
                                           join um in context.UnitOfMeasurements on g.UnitOfMeasurement equals um.Name
                                           where ii.InvoiceId == modelInvoiceVM.Id
                                           select new
                                           {
                                               ii.InvoiceId,
                                               ii.GoodsOrServicesId,
                                               ii.UniqueIdentifier,
                                               ii.PriceUnit,
                                               ii.Count,
                                               ii.GoodsOrServicesDescripton,
                                               ii.Discount,
                                               ii.ValueAddedPercentage,
                                               g.Name,
                                               g.UnitOfMeasurement,
                                               um.Code
                                           }).ToList();

                        int pamentType = invoiceForEdit.PaymentType switch
                        {
                            "نقدی" => 1,
                            "نسیه" => 2,
                            _ => 0
                        };
                        if (pamentType == 0)
                            return new ApiModel { Data = null, Status = 400, Success = false,
                                Message = "نوع پرداخت صورتحساب نامعتبر است؛ فقط «نقدی» یا «نسیه» قابل ارسال است." };

                        var typeSettleErr = ValidateSettlementForType(invoiceForEdit.TypePattern, pamentType);

                        if (typeSettleErr is not null)

                            return new ApiModel { Data = null, Status = 400, Success = false, Message = typeSettleErr };

                        var customer = context.Customers.Where(c => c.Id == invoiceForEdit.CustomerId).FirstOrDefault();
                        var cpInfo = context.CPInfos.Where(c => c.Id == invoice.CpInfo).FirstOrDefault();

                        string taxNumber = string.IsNullOrEmpty(invoiceForEdit.NewTaxNumber) ? invoiceForEdit.TaxNumber : invoiceForEdit.NewTaxNumber;

                        var bodyItems = invoiceItem.Select(item =>
                        {
                            return new InvoiceBodyDto
                            {
                                Sstid = item.UniqueIdentifier,//شناسه کالا/خدمت
                                                              // Sstt = item.Name,

                                Sstt = item.Name + (item.GoodsOrServicesDescripton != null ? " " + item.GoodsOrServicesDescripton : ""),
                                Mu = item.Code,      // کد واحد معتبر
                                Am = Math.Truncate(item.Count),
                                Fee = Math.Truncate(item.PriceUnit),
                                Prdis = Math.Truncate(item.Count * item.PriceUnit),
                                Dis = Math.Truncate(item.Discount),
                                Adis = Math.Truncate(item.Count * item.PriceUnit - item.Discount),
                                Vra = item.ValueAddedPercentage,
                                Vam = Math.Truncate((item.Count * item.PriceUnit - item.Discount) / 100 * item.ValueAddedPercentage),
                                Tsstam = Math.Truncate((item.Count * item.PriceUnit - item.Discount) + ((item.Count * item.PriceUnit - item.Discount) / 100 * item.ValueAddedPercentage))
                            };

                        }).ToList();
                        var header = new InvoiceHeaderDto
                        {
                            Irtaxid = taxNumber,
                            Taxid = taxId1,
                            Inno = inno1,
                            Indatim = indatim1,
                            Inty = SafeInvoiceType(invoiceForEdit.TypePattern), // نوع صورتحساب (سرادم): ۱/۲/۳
                            Inp = 1, // الگوی فروش عادی
                            Ins = 4,
                            Tins = cpInfo.EconomicNumber,
                            Tinb = IsNoBuyerDisclosureType(SafeInvoiceType(invoiceForEdit.TypePattern)) ? null : customer.EconomicNumber,
                                Bid = IsNoBuyerDisclosureType(SafeInvoiceType(invoiceForEdit.TypePattern)) ? null : customer.NationalID,
                                Tob = IsNoBuyerDisclosureType(SafeInvoiceType(invoiceForEdit.TypePattern)) ? null : (customer.Type == "حقیقی" ? 1 : 2),
                                Bpc = IsNoBuyerDisclosureType(SafeInvoiceType(invoiceForEdit.TypePattern)) ? null : customer.PostalCode,
                            Tprdis = bodyItems.Sum(x => x.Prdis),
                            Tdis = bodyItems.Sum(x => x.Dis),
                            Tadis = bodyItems.Sum(x => x.Adis),
                            Tvam = bodyItems.Sum(x => x.Vam),
                            Todam = 0,
                            Tbill = bodyItems.Sum(x => x.Tsstam),
                            Setm = pamentType

                        };

                        InvoiceDto invoice2 = new InvoiceDto
                        {
                            Header = header,
                            Body = bodyItems
                        };
                        List<InvoiceDto> invoiceList = new List<InvoiceDto>();
                        invoiceList.Add(invoice2);
                        var response = TaxApiService.Instance.TaxApis.SendInvoices(
                  invoiceList,
                  null);
                        var packetResponse = response?.Body?.Result?.FirstOrDefault();
                        if (packetResponse is null)
                        {
                            var sendErr = ExtractSdkErrors(response);
                            return new ApiModel
                            {
                                Data = null,
                                Status = 500,
                                Success = false,
                                Message = "ارسال صورتحساب به سامانهٔ مودیان ناموفق بود و آن را «ارسال شده» علامت نزنیم." +
                                    (string.IsNullOrEmpty(sendErr) ? " پاسخی از سامانه برنگشت." : $"\nفهرست خطای برگشتی سامانه: {sendErr}")
                            };
                        }
                        var uid = packetResponse.Uid;
                        var referenceId = packetResponse.ReferenceNumber;

                        ///وضعیت ارسال صورتحساب به مودیان
                        var uidAndFiscalId = new UidAndFiscalId(uid, conection.TaxMemoryID);
                        var inquiryResultModels =
                        TaxApiService.Instance.TaxApis.InquiryByUidAndFiscalId(new(){uidAndFiscalId
                        });


                        invoiceForEdit.ShowButtons = false;

                        var newInvoiceForEdit = context.Invoices.Where(c => c.Id == modelInvoiceVM.Id).FirstOrDefault();
                        newInvoiceForEdit.NewTaxNumber = taxId1;
                        newInvoiceForEdit.TaxNumber = taxNumber;
                        newInvoiceForEdit.ReferenceId = referenceId;

                        context.SaveChanges();
                        model.Data = invoiceForEdit;
                        model.Status = 201;
                        model.Success = true;
                        model.Message = MessageCode.StatusCode.TryGetValue(201, out string messagerr) == true
                            ? model.Message = messagerr
                            : model.Message = "کد وضعیت نامعتبر است.";
                        return model;
                    }

                // محافظت از مسیرهای بازگشت؛ اگر نوع عملیات در آینده توسعه یافت نیز خروجی ApiModel تضمین شود.
                return new ApiModel { Data = null, Status = 400, Success = false,
                    Message = "نوع عملیات صورتحساب پشتیبانی نمی‌شود." };
            }
            catch (Exception ex)
            {

                model.Data = null;
                model.Message = MessageCode.StatusCode.TryGetValue(500, out string messager) == true
                    ? model.Message = messager
                    : model.Message = ex.Message;
                model.Status = 500;
                model.Success = false;
                return model;
            }


        }
        public async Task<ApiModel> SabtInvoiceAsync(InvoiceVM modeldata)
        {
            if (await GuardCpInfoAsync(modeldata.CPInfoId ?? 0) is { } guard) return guard;
            if (modeldata.IsNew)
                return await CreateInvoiceAsync(modeldata);

            return await UpdateInvoiceAsync(modeldata);
        }

        /// <summary>محاسبهٔ داخلیٔ شمارهٔ صورتحساب بعدی یک شرکت (حداکثر + ۱).</summary>
        private async Task<int> NextInvoiceNoCoreAsync(int? cpinfo)
        {
            int? max = await context.Invoices
                .Where(i => i.CpInfo == cpinfo)
                .MaxAsync(i => (int?)i.InvoiceNumber);
            return (max ?? 0) + 1;
        }

        /// <summary>
        /// نوع صورتحساب (نوع ۱/۲/۳) — طبق مشخصهٔ رسمی سرادم به فیلد `inty` سرفرستاده می‌شود:
        /// ۱ = با افشای اطلاعات خریدار | ۲ و ۳ = بدون افشای اطلاعات خریدار.
        /// مقدار صفر/نامعتبر (رکوردهای قدیمی) به‌عنوان نوع ۱ در نظر گرفته می‌شود.
        /// </summary>
        private static int SafeInvoiceType(int typePattern) => typePattern is 2 or 3 ? typePattern : 1;

        /// <summary>نوع ۲/۳ افشای اطلاعات خریدار ندارند و تسویهٔ آن‌ها همیشه نقدی است.</summary>
        private static bool IsNoBuyerDisclosureType(int type) => type is 2 or 3;

        /// <summary>اعتبارسنجی قبل از ارسال: صورتحساب نوع ۲/۳ فقط با تسویهٔ نقدی قابل ارسال است.</summary>
        private static string? ValidateSettlementForType(int typePattern, int pamentType)
        {
            if (IsNoBuyerDisclosureType(SafeInvoiceType(typePattern)) && pamentType != 1)
                return "صورتحساب نوع دوم/سوم (بدون افشای اطلاعات خریدار) فقط با نوع پرداخت «نقدی» قابل ارسال است؛ نوع پرداخت را «نقدی» کنید.";
            return null;
        }

        /// <summary>تبدیل رشتهٔ «نوع اول/دوم/سوم» فرم به کد عددی؛ خالی/نامشخص -> ۱ (نوع اول).
        /// نرمال‌سازی می‌شود تا فاصله/نیم‌فاصله یا ارسال کد عددی («2»/«3») هم درست تشخیص داده شود.</summary>
        private static int TypePatternToCode(string? typePattern)
        {
            var t = (typePattern ?? string.Empty).Trim().Replace('\u200c', ' ').Replace("  ", " ");
            if (t == "2" || t.Contains("دوم")) return 2;
            if (t == "3" || t.Contains("سوم")) return 3;
            return 1;
        }

        /// <summary>پیش‌نمایش شمارهٔ صورتحساب بعدی (برای نمایش در فرم ثبت).</summary>
        public async Task<ApiModel> NextInvoiceNumberAsync(int cpinfoId)
        {
            if (await GuardCpInfoAsync(cpinfoId) is { } guard) return guard;
            ApiModel model = new ApiModel();
            try
            {
                model.Data = await NextInvoiceNoCoreAsync(cpinfoId);
                model.Status = 200;
                model.Message = MessageCode.StatusCode.TryGetValue(200, out string messager)
                    ? messager : "کد وضعیت نامعتبر است.";
                model.Success = true;
                return model;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error in NextInvoiceNumberAsync: {e.Message}");
                return CreateErrorResponse(500);
            }
        }

        public async Task<ApiModel> GetGoodOrServiceForComboAsync(int cpinfoid)
        {
            if (await GuardCpInfoAsync(cpinfoid) is { } guard) return guard;
            ApiModel model = new ApiModel();
            var result = await context.GoodsOrServices.Where(c => c.CpInfo == cpinfoid && !c.IsDelete).OrderBy(c => c.Name).Select(c => new VMGoodsOrServicesList
            {
                Name = c.Name,
                GoodsOrServicesId = c.Id,
                Price = c.Price,
                UniqueIdentifier = c.UniqueIdentifier,
                ValueAddedPercentage = c.ValueAddedPercentage,
                UnitOfMeasurement = c.UnitOfMeasurement

            }).ToListAsync();

            if (result.Any())
            {
                model.Data = result;
                model.Status = 200;
                model.Message = MessageCode.StatusCode.TryGetValue(200, out string messager) == true
                    ? model.Message = messager
                    : model.Message = "کد وضعیت نامعتبر است.";
                model.Success = true;
                return model;
            }

            else
            {
                // لیست خالی خطا نیست: مثلاً شرکت مودیانی که هنوز کالا/مشتری/واحدی ندارد.
                // قبلاً اینجا 404 برمی‌گشت و کلاینت «مورد درخواستی یافت نشد» نشان می‌داد.
                model.Data = result;
                model.Message = MessageCode.StatusCode.TryGetValue(200, out string messager) == true
                    ? model.Message = messager
                    : model.Message = "کد وضعیت نامعتبر است.";
                model.Status = 200;
                model.Success = true;

                return model;
            }
        }

        public async Task<ApiModel> GetCustomerForComboAsync(int cpinfoid)
        {
            if (await GuardCpInfoAsync(cpinfoid) is { } guard) return guard;
            ApiModel model = new ApiModel();
            var result = await context.Customers.Where(c => c.CpInfoId == cpinfoid && !c.IsDelete).OrderBy(c => c.Name).Select(c => new VMCustomerList
            {
                Name = c.Name,
                CustomerId = c.Id,
                PostalCode = c.PostalCode,
                Phone = c.Phone,
                NationalID = c.NationalID,
                CpInfoId = (int)c.CpInfoId,
                Type = c.Type,
                EconomicNumber = c.EconomicNumber,
                Address = c.Address,
            }).ToListAsync();

            if (result.Any())
            {
                model.Data = result;
                model.Status = 200;
                model.Message = MessageCode.StatusCode.TryGetValue(200, out string messager) == true
                    ? model.Message = messager
                    : model.Message = "کد وضعیت نامعتبر است.";
                model.Success = true;
                return model;
            }

            else
            {
                // لیست خالی خطا نیست: مثلاً شرکت مودیانی که هنوز کالا/مشتری/واحدی ندارد.
                // قبلاً اینجا 404 برمی‌گشت و کلاینت «مورد درخواستی یافت نشد» نشان می‌داد.
                model.Data = result;
                model.Message = MessageCode.StatusCode.TryGetValue(200, out string messager) == true
                    ? model.Message = messager
                    : model.Message = "کد وضعیت نامعتبر است.";
                model.Status = 200;
                model.Success = true;

                return model;
            }
        }

        public async Task<ApiModel> GetUnitOfMeasurementForComboAsync(int cpinfoid)
        {
            if (await GuardCpInfoAsync(cpinfoid) is { } guard) return guard;
            ApiModel model = new ApiModel();
            var result = await context.UnitOfMeasurements
                .Where(c => c.CpInfo == cpinfoid && !c.IsDelete)
                .OrderBy(c => c.Name).Select(c => new VMUnitOfMeasurement
            {
                Name = c.Name,
                UnitOfMeasurementId = c.Id,
                CpInfoId = cpinfoid
            }).ToListAsync();

            if (result.Any())
            {
                model.Data = result;
                model.Status = 200;
                model.Message = MessageCode.StatusCode.TryGetValue(200, out string messager) == true
                    ? model.Message = messager
                    : model.Message = "کد وضعیت نامعتبر است.";
                model.Success = true;
                return model;
            }

            else
            {
                // لیست خالی خطا نیست: مثلاً شرکت مودیانی که هنوز کالا/مشتری/واحدی ندارد.
                // قبلاً اینجا 404 برمی‌گشت و کلاینت «مورد درخواستی یافت نشد» نشان می‌داد.
                model.Data = result;
                model.Message = MessageCode.StatusCode.TryGetValue(200, out string messager) == true
                    ? model.Message = messager
                    : model.Message = "کد وضعیت نامعتبر است.";
                model.Status = 200;
                model.Success = true;

                return model;
            }
        }

        public async Task<ApiModel> UpdateGoodOrServiceAsync(VMGoodsOrService modeldata)
        {
            if (await GuardCpInfoAsync(modeldata.CpInfoId) is { } guard) return guard;
            ApiModel model = new ApiModel();
            if (modeldata.UniqueIdentifier == "" || modeldata.Price == 0 ||
            modeldata.Name == "" || modeldata.GoodsOrServicesId == 0)
            {
                model.Data = null;
                model.Status = 400;
                model.Message = "نام کالا، شناسهٔ CPC و قیمت واحد (بیشتر از صفر) الزامی است.";
                model.Success = false;
                return model;
            }
            else
            {
                try
                {
                    var data = await context.GoodsOrServices.FindAsync(modeldata.GoodsOrServicesId);
                    // 🔴 باگ قبلی: نال‌چک نبود (واحد 0 → 500) و پس از یافتن، در ستون UnitOfMeasurement ذخیره نمی‌شد؛
                    var unit = modeldata.UnitOfMeasurementId > 0
                        ? context.UnitOfMeasurements.FirstOrDefault(c => c.Id == modeldata.UnitOfMeasurementId)?.Name ?? modeldata.UnitOfMeasurement
                        : modeldata.UnitOfMeasurement;
                    if (data is not null)
                    {
                        data.Price = modeldata.Price;
                        data.Name = modeldata.Name;
                        data.CpInfo = modeldata.CpInfoId;
                        data.UniqueIdentifier = modeldata.UniqueIdentifier;
                        if (!string.IsNullOrEmpty(unit)) data.UnitOfMeasurement = unit;
                        data.ValueAddedPercentage = Convert.ToByte(Math.Clamp(modeldata.ValueAddedPercentage ?? 0, 0, 100));
                        await context.SaveChangesAsync();
                        model.Data = data;
                        model.Status = 201;
                        model.Message = MessageCode.StatusCode.TryGetValue(201, out string messager) == true
                            ? model.Message = messager
                            : model.Message = "کد وضعیت نامعتبر است.";
                        model.Success = true;
                    }









                    return model;

                }
                catch (Exception)
                {
                    model.Data = null;
                    model.Status = 500;
                    model.Message = MessageCode.StatusCode.TryGetValue(500, out string messager) == true
                        ? model.Message = messager
                        : model.Message = "کد وضعیت نامعتبر است.";
                    model.Success = false;

                    return model;
                }
            }
        }

        public async Task<ApiModel> GetGoodOrServiceByIdAsync(int Id)
        {
            ApiModel model = new ApiModel();
            var result = await context.GoodsOrServices.FirstOrDefaultAsync(c => c.Id == Id);
            var UnitOfMeasurement = await context.UnitOfMeasurements.FirstOrDefaultAsync(c => c.Name == result.UnitOfMeasurement);
            if (result is not null)
            {
                var respose = new VMGoodsOrService
                {
                    Name = result.Name,
                    GoodsOrServicesId = result.Id,
                    Price = result.Price,
                    CpInfoId = result.CpInfo,
                    UnitOfMeasurementId = UnitOfMeasurement.Id,
                    ValueAddedPercentage = result.ValueAddedPercentage,
                    UniqueIdentifier = result.UniqueIdentifier,
                };
                model.Data = respose;
                model.Status = 200;
                model.Message = MessageCode.StatusCode.TryGetValue(200, out string messager) == true
                    ? model.Message = messager
                    : model.Message = "کد وضعیت نامعتبر است.";
                model.Success = true;
                return model;
            }




            else
            {
                model.Data = null;
                model.Message = MessageCode.StatusCode.TryGetValue(404, out string messager) == true
                    ? model.Message = messager
                    : model.Message = "کد وضعیت نامعتبر است.";
                model.Status = 404;
                model.Success = false;

                return model;
            }
        }

        public async Task<ApiModel> GetInvoiceByIdAsync(int InvoiceId)
        {
            ApiModel model = new ApiModel();
            string? date = "";
            try
            {

                InvoiceVM invoiceInfo = new InvoiceVM();
                if (InvoiceId is not 0)
                {
                    var invoice = await context.Invoices.Include(c => c.fiscalYear).FirstOrDefaultAsync(c => c.Id == InvoiceId && !c.IsDelete);
                    if (invoice is null)
                        return new ApiModel { Data = null, Status = 404, Success = false, Message = "صورتحساب یافت نشد." };
                    // صورتحساب خدمات‌دهندهٔ شرکت دیگر برای این کاربر «یافت‌شده» محسوب نمی‌شود.
                    if (await GuardCpInfoAsync(invoice.CpInfo ?? 0) is not null)
                        return new ApiModel { Data = null, Status = 404, Success = false, Message = "صورتحساب یافت نشد." };

                    //// در Controller یا ViewModel
                    ///var fiscal = await context.FiscalYears.FindAsync(invoice.FiscalYearId);

                    if (invoice.fiscalYear != null)
                    {
                        // سال شمسی دقیق بر اساس تاریخ شروع
                        date = PersianDateHelper.ToShamsi(invoice.fiscalYear.StartDate, "yyyy");

                        //   var FiscalYear = shamsiYear;                    // خروجی: 1405
                        //date= PersianDateHelper.ToShamsi(fiscal.StartDate);
                        //Model.EndDateShamsi = PersianDateHelper.ToShamsi(fiscal.EndDate);

                        // نمایش کامل
                        //Model.FiscalYearDisplay = $"{shamsiYear} ({Model.StartDateShamsi} تا {Model.EndDateShamsi})";
                    }

                    // شناسهٔ رکورد باید در DTO برگردد؛ بدون آن کلاینت مقدار پیش‌فرض ۰ می‌فرستد
                    // و SendInvoiceAsync نه فاکتور را پیدا می‌کند و نه می‌تواند TaxId/Inno بسازد.
                    invoiceInfo.Id = invoice.Id;
                    invoiceInfo.CustomerId = invoice.CustomerId;
                    invoiceInfo.TaxNumber = invoice.TaxNumber;
                    invoiceInfo.PaymentType = invoice.PaymentType;
                    invoiceInfo.CPInfoId = context.CPInfos.First(c => c.Id == invoice.CpInfo).Id;
                    invoiceInfo.IsNew = false;
                    invoiceInfo.NumberInvoice = invoice.InvoiceNumber;
                    invoiceInfo.FiscalYearId = date;
                    invoiceInfo.DateEndFiscalYear = invoice.fiscalYear?.EndDate;
                    invoiceInfo.DatestartFiscalYear = invoice.fiscalYear?.StartDate;

                    invoiceInfo.DateOrder = invoice.DateOrder;
                    invoiceInfo.TypePattern = invoice.TypePattern == 1 ? "نوع اول" : invoice.TypePattern == 2 ? "نوع دوم" : "نوع سوم";
                    invoiceInfo.Description = invoice.Description;
                    invoiceInfo.Name = invoice.Subject;
                    invoiceInfo.TaxMemoryID = context.ConnectInfos.FirstOrDefault(c => c.CpInfo == invoice.CpInfo)?.TaxMemoryID;
                    invoiceInfo.NewTaxNumber = invoice.NewTaxNumber;

                    // ابتدا مقدار NewTaxNumber رو بررسی کن، اگر خالی بود از TaxNumber استفاده کن

                    //// مقدار NewTaxNumber رو هم در invoiceInfo ذخیره کن


                    invoiceInfo.Subject = invoice.Subject;
                    invoiceInfo.StatusSentToSystem = invoice.StatusSentToSystem == "0" ? "ارسال نشده" :
            invoice.StatusSentToSystem == "1" ? "ارسال شده در انتظار استعلام" :
            invoice.StatusSentToSystem == "2" ? "در انتظار بررسی کارپوشه" :
            invoice.StatusSentToSystem == "3" ? "ارسال شده" :
            invoice.StatusSentToSystem == "4" ? "ارسال شده در انتظار واکنش" :
           invoice.StatusSentToSystem == "5" ? "دارای خطا" :
           invoice.StatusSentToSystem == "6" ? "بدون نیاز به واکنش" :
           invoice.StatusSentToSystem == "7" ? "تأیید شده" :
           invoice.StatusSentToSystem == "8" ? "رد شده" : "وضعیت نامشخص";
                    invoiceInfo.StatusSentToSystemInt = invoice.StatusSentToSystem;

                    invoiceInfo.InvoiceItems = new ObservableCollection<VmInvoiceItems>(
                        context.InvoiceItems
                               .Where(c => c.InvoiceId == invoice.Id)
                               .Select(i => new VmInvoiceItems
                               {
                                   InvoiceId = i.InvoiceId,
                                   DescriptionKala=i.GoodsOrServicesDescripton,
                                   Name = context.GoodsOrServices.FirstOrDefault(c => c.Id == i.GoodsOrServicesId).Name,
                                   UniqueIdentifier = context.GoodsOrServices.FirstOrDefault(c => c.Id == i.GoodsOrServicesId).UniqueIdentifier,
                                   UnitOfMeasurement = context.GoodsOrServices.FirstOrDefault(c => c.Id == i.GoodsOrServicesId).UnitOfMeasurement,
                                   GoodsOrServicesId = i.GoodsOrServicesId,
                                   PriceUnit = i.PriceUnit,
                                   Discount = i.Discount,
                                   ValueAddedPercentage = i.ValueAddedPercentage,
                                   ValueAddedAmount = ((i.Count * i.PriceUnit) - i.Discount) * i.ValueAddedPercentage / 100,
                                   Count = i.Count,
                                   PriceNaghd = invoice.PriceNaghd,
                                   PriceNesye = invoice.PriceNesye,
                                   CalculatedPay = (decimal)(
    (((i.Count * i.PriceUnit) - i.Discount))
    +
    ((((i.Count * i.PriceUnit) - i.Discount))
        * i.ValueAddedPercentage / 100m)
)
                               })
                               .ToList()
                    );
                    invoiceInfo.PriceNesye = invoiceInfo.InvoiceItems.Sum(c => c.PriceNesye);
                    invoiceInfo.PriceNaghd = invoiceInfo.InvoiceItems.Sum(c => c.PriceNaghd);
                    model.Data = invoiceInfo;
                    model.Status = 200;
                    model.Message = MessageCode.StatusCode.TryGetValue(200, out string messager) == true
                        ? model.Message = messager
                        : model.Message = "کد وضعیت نامعتبر است.";
                    model.Success = true;
                    return model;
                }
                else
                {
                    model.Data = null;
                    model.Message = MessageCode.StatusCode.TryGetValue(404, out string messager) == true
                        ? model.Message = messager
                        : model.Message = "کد وضعیت نامعتبر است.";
                    model.Status = 404;
                    model.Success = false;

                    return model;
                }
            }
            catch (Exception)
            {

                model.Data = null;
                model.Status = 500;
                model.Message = MessageCode.StatusCode.TryGetValue(500, out string messager) == true
                    ? model.Message = messager
                    : model.Message = "کد وضعیت نامعتبر است.";
                model.Success = false;

                return model;
            }
        }
        public class TaxInquiryResult
        {
            public string ReferenceNumber { get; set; }
            public decimal TaxAmount { get; set; }
            public DateTime InquiryDate { get; set; }
            public string Status { get; set; }
        }
        public async Task<ApiModel> EstelamInvoiceAsync(string ReferenceId, int InvoiceId, string dateorder)
        {
            ApiModel model = new ApiModel();
            try
            {
                List<string> referenceNumbers = new List<string> { ReferenceId };
                var invoice = context.Invoices.FirstOrDefault(c => c.Id == InvoiceId);
                if (invoice is not null && await GuardCpInfoAsync(invoice.CpInfo ?? 0) is { } guard) return guard;
                if (await IsConnectApi((int)invoice.CpInfo)) // استفاده از await به جای .Result
                {
                    var inquiryResultModel = TaxApiService.Instance.TaxApis
                        .InquiryByReferenceId(referenceNumbers);

                    var item = inquiryResultModel.FirstOrDefault();


                    string invoiceResult = item?.Status ?? "";
                    //Console.WriteLine("status invoice*******************************************");
                    //Console.WriteLine(item.PacketType);
                    //Console.WriteLine(item.Data);
                    //Console.WriteLine(item.Status);
                    // دریافت فاکتور جاری
                    var currentInvoice = await context.Invoices
                        .FirstOrDefaultAsync(c => c.Id == InvoiceId);

                    if (currentInvoice == null)
                    {
                        model.Status = 404;
                        model.Success = false;
                        model.Message = "فاکتور یافت نشد";
                        return model;
                    }

                    switch (invoiceResult)
                    {
                        case "IN_PROGRESS":
                            currentInvoice.StatusSentToSystem = "2";
                            model.Data = "عدم پاسخگویی سامانه";
                            break;

                        case "PENDING":
                            // در کارپوشه درج شده و در انتظار واکنش خریدار
                            currentInvoice.StatusSentToSystem = "4";
                            model.Data = "در انتظار واکنش";
                            break;

                        case "RECEIVE_INVOICE_CONFIRM":
                        case "SYSTEM_CONFIRM":
                            // تأیید دستی خریدار یا تأیید سیستمی (پایان مهلت ۳۰ روزه)
                            currentInvoice.StatusSentToSystem = "7";
                            currentInvoice.ShowButtons = true;
                            model.Data = "تأیید شده";
                            break;

                        case "REJECT":
                            // رد توسط خریدار — فروشنده باید ابطال یا اصلاحی صادر کند
                            currentInvoice.StatusSentToSystem = "8";
                            currentInvoice.ShowButtons = true;
                            model.Data = "رد شده";
                            break;

                        case "FAILED":
                            currentInvoice.StatusSentToSystem = "5";
                            currentInvoice.ShowButtons = true; // اجازهٔ اصلاح/ارسال مجدد پس از خطای قطعی
                            model.Data = "دارای خطا";
                            break;

                        case "SUCCESS":
                            currentInvoice.StatusSentToSystem = "4";
                            model.Data = "ارسال شده";

                            break;

                        default:
                            model.Status = 500;
                            model.Success = false;
                            model.Message = "کد وضعیت نامعتبر است.";
                            return model;
                    }

                    await context.SaveChangesAsync();

                    model.Status = 200;
                    model.Success = true;
                    model.Message = MessageCode.StatusCode
                        .TryGetValue(200, out string message) ? message : "عملیات موفق";
                }
                else
                {
                    model.Status = 500;
                    model.Success = false;
                    model.Message = "عدم اتصال به سامانه";
                }
            }
            catch (Exception ex)
            {
                model.Data = null;
                model.Message = MessageCode.StatusCode
                    .TryGetValue(500, out string message) ? message : ex.Message;
                model.Status = 500;
                model.Success = false;
            }

            return model;
        }


        public async Task<ApiModel> ErrorInvoiceAsync(string referenceId)
        {
            var model = new ApiModel();

            try
            {
                //var connection = ConnectInfo();

                //var isConnect = await IsConnectAsync(
                //    connection.TaxMemoryID,
                //    connection.PrivateKeyPath,
                //    connection.WebServiceAddress);
                var invoice = context.Invoices.FirstOrDefault(c => c.ReferenceId.Equals(referenceId));
                if (await IsConnectApi((int)invoice.CpInfo) == false)
                {
                    model.Success = false;
                    model.Status = 500;
                    model.Message = MessageCode.StatusCode.TryGetValue(500, out var msg)
                        ? msg
                        : "کد وضعیت نامعتبر است.";

                    return model;
                }

                var referenceNumbers = new List<string> { referenceId };

                var inquiryResult =
                    TaxApiService.Instance.TaxApis.InquiryByReferenceId(referenceNumbers);

                if (inquiryResult == null || !inquiryResult.Any())
                {
                    model.Success = false;
                    model.Status = 404;
                    model.Message = "اطلاعاتی یافت نشد";
                    return model;
                }

                foreach (var item in inquiryResult)
                {
                    if (item.Status != "FAILED")
                        continue;

                    if (item.Data == null)
                        continue;

                    var errorResult =
                        JsonConvert.DeserializeObject<InquiryErrorResponse>(
                            item.Data.ToString());

                    var firstError = errorResult?.error?.FirstOrDefault();

                    model.Data = new ErroreMesage
                    {
                        code = firstError?.code,
                        message = firstError?.message,
                        errorsAndWarnings = string.Empty
                    };

                    model.Success = false;
                    model.Status = 400;
                    model.Message = "صورتحساب دارای خطا است";

                    return model;
                }

                model.Success = true;
                model.Status = 200;
                model.Message = "صورتحساب بدون خطا است";
                model.Data = null;

                return model;
            }
            catch (Exception ex)
            {
                model.Success = false;
                model.Status = 500;
                model.Data = null;
                model.Message = ex.Message;

                return model;
            }
        }
        public async Task<ApiModel> ConnectionApiAsync(VMConnection connection, IFormFile formFile)
        {
            if (await GuardCpInfoAsync(connection.cpinfoid) is { } guard) return guard;
            // اعتبارسنجی اولیه
            if (formFile == null || string.IsNullOrEmpty(connection.TaxMemoryID))
            {
                return CreateErrorResponse(400);
            }

            try
            {
                // ذخیره فایل — در wwwroot/uploads/{نام شرکت}
                string savedFileName = await SaveFileAsync(formFile, connection.cpinfoid);

                // غیرفعال کردن رکوردهای قبلی
                await DeactivatePreviousConnectionsAsync(connection.cpinfoid);

                // ایجاد ارتباط جدید
                var newConnectionId = await CreateNewConnectionAsync(connection, savedFileName);

                // بازگشت پاسخ موفقیت
                return CreateSuccessResponse(connection, newConnectionId);
            }
            catch (Exception ex)
            {
                // لاگ خطا (بهتر است از ILogger استفاده شود)
                Console.WriteLine($"Error in ConnectionApiAsync: {ex.Message}");
                return CreateErrorResponse(500);
            }
        }

        // ---------- ابزارهای استخراج جزئیات خطای SDK سامانه (بازتابی — بدون وابستگی به ساختار دقیق نسخهٔ SDK) ----------
        private static string ExceptionDetails(Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            var cur = ex;
            var depth = 0;
            while (cur != null && depth < 5)
            {
                if (depth == 0) sb.Append(cur.GetType().Name).Append(": ").Append(cur.Message);
                else sb.Append(" ← ").Append(cur.GetType().Name).Append(": ").Append(cur.Message);
                cur = cur.InnerException;
                depth++;
            }
            return sb.ToString();
        }

        // بعضی استثناهای SDK خودشان مدل خطای سامانه را در Data/Property نگه می‌دارند
        private static string ExtractSdkErrorsFromException(Exception ex)
        {
            if (ex is null) return string.Empty;
            var parts = new List<string>();
            if (ex.Data != null && ex.Data.Count > 0)
                foreach (System.Collections.DictionaryEntry d in ex.Data) parts.Add($"{d.Key}={d.Value}");
            var nested = ExtractSdkErrors(ex);
            if (!string.IsNullOrEmpty(nested)) parts.Add(nested);
            return string.Join(" | ", parts.Distinct());
        }

        // مدل پاسخ SDK (ApiResultPack/TokenModel/PacketResponse و…) به‌صورت بازتابی پویش می‌شود؛ اگر فهرست خطا دارد نمایش داده می‌شود.
        private static string ExtractSdkErrors(object? sdkResult)
        {
            if (sdkResult is null) return string.Empty;
            var parts = new List<string>();
            foreach (var name in new[] { "Errors", "ErrorList", "Error", "ErrorsAndWarnings", "ErrorMessage", "Message", "Description", "ExceptionMessage", "MessageError" })
            {
                var p = sdkResult.GetType().GetProperty(name);
                if (p is null) continue;
                object? val;
                try { val = p.GetValue(sdkResult); } catch { continue; }
                if (val is null) continue;
                if (val is string s)
                {
                    if (!string.IsNullOrWhiteSpace(s)) parts.Add(s);
                    continue;
                }
                if (val is System.Collections.IEnumerable coll)
                {
                    var i = 0;
                    foreach (var item in coll)
                    {
                        if (item is null) continue;
                        parts.Add(DescribeSdkError(item));
                        if (++i >= 10) break;
                    }
                    continue;
                }
                parts.Add(DescribeSdkError(val));
            }
            return string.Join(" | ", parts.Distinct());
        }

        private static string DescribeSdkError(object e)
        {
            var t = e.GetType();
            var code = t.GetProperty("Code")?.GetValue(e)?.ToString();
            var msg = (t.GetProperty("Message")?.GetValue(e)?.ToString()
                       ?? t.GetProperty("Detail")?.GetValue(e)?.ToString()
                       ?? t.GetProperty("Description")?.GetValue(e)?.ToString());
            if (string.IsNullOrWhiteSpace(msg)) return e.ToString() ?? string.Empty;
            return string.IsNullOrWhiteSpace(code) ? msg : $"{code}: {msg}";
        }

        #region Private Methods

        /// <summary>نام امن برای پوشهٔ شرکت روی دیسک (بدون کاراکترهای namaجاز مسیر).</summary>
        private static string SafeFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.Trim().Select(c => (invalid.Contains(c) || c is ' ' or '/' or '\\' or ':') ? '-' : c).ToArray();
            var s = new string(chars);
            while (s.Contains("--")) s = s.Replace("--", "-");
            return s.Trim('-');
        }

        /// <summary>پوشهٔ کلید هر شرکت (خدمات‌دهنده): wwwroot/uploads/{نام امن شرکت}</summary>
        private async Task<string> GetKeyFolderAsync(int cpinfoId)
        {
            var companyName = await context.CPInfos.Where(c => c.Id == cpinfoId)
                .Select(c => c.PersianName).FirstOrDefaultAsync();
            return SafeFolderName(!string.IsNullOrWhiteSpace(companyName) ? companyName! : $"cp{cpinfoId}");
        }

        /// <summary>مسیر کامل کلید ذخیره‌شده؛ اول مسیر جدید uploads، سپس مسیر قدیمی File/Moadian برای رکوردهای سابق.</summary>
        private async Task<string> ResolveExistingKeyPathAsync(int cpinfoId, string privateKeyPath)
        {
            var folder = await GetKeyFolderAsync(cpinfoId);
            var latest = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", folder, privateKeyPath);
            if (System.IO.File.Exists(latest)) return latest;
            return Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "File", "Moadian", privateKeyPath);
        }

        private async Task<string> SaveFileAsync(IFormFile formFile, int cpinfoId)
        {
            // ایجاد نام فایل منحصربه‌فرد
            string hashName = SecurityHelper.EncryptString(formFile.FileName);
            string fileName = $"{hashName}{Path.GetExtension(formFile.FileName)}";

            // مسیر ذخیره‌سازی — wwwroot/uploads/{نام شرکت}/ برای هر خدمات‌دهنده
            string folder = await GetKeyFolderAsync(cpinfoId);
            string uploadDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", folder);

            // ایجاد دایرکتوری در صورت نیاز
            Directory.CreateDirectory(uploadDir);

            // مسیر کامل فایل
            string filePath = Path.Combine(uploadDir, fileName);

            // ذخیره فایل (خام — قابل دانلود مستقیم)
            using (FileStream stream = new FileStream(filePath, FileMode.Create))
            {
                await formFile.CopyToAsync(stream);
            }

            return fileName;
        }

        private async Task DeactivatePreviousConnectionsAsync(int cpInfoId)
        {
            var previousConnections = await context.ConnectInfos
                .Where(c => c.CpInfo == cpInfoId && !c.IsDelete)
                .ToListAsync();

            foreach (var item in previousConnections)
            {
                item.IsDelete = true;
            }

            // اگر رکوردی وجود داشت، SaveChanges انجام شود
            if (previousConnections.Any())
            {
                await context.SaveChangesAsync();
            }
        }

        private async Task<int> CreateNewConnectionAsync(VMConnection connection, string fileName)
        {
            var connectInfo = new ConnectInfo()
            {
                PrivateKeyPath = fileName,
                TaxMemoryID = connection.TaxMemoryID,
                CpInfo = connection.cpinfoid,
                WebServiceAddress = connection.WebServiceAddress
            };

            context.ConnectInfos.Add(connectInfo);
            await context.SaveChangesAsync();

            return connectInfo.Id;
        }

        private ApiModel CreateErrorResponse(int statusCode)
        {
            string message = MessageCode.StatusCode.TryGetValue(statusCode, out string msg)
                ? msg
                : "کد وضعیت نامعتبر است.";

            return new ApiModel
            {
                Data = false,
                Status = statusCode,
                Success = false,
                Message = message
            };
        }

        private ApiModel CreateSuccessResponse(VMConnection connection, int newConnectionId)
        {
            string message = MessageCode.StatusCode.TryGetValue(201, out string msg)
                ? msg
                : "کد وضعیت نامعتبر است.";

            return new ApiModel
            {
                Data = connection,
                Status = 201,
                Success = true,
                Message = message,
                SematId = newConnectionId
            };
        }

        #endregion
        //public async Task<ApiModel> ConnectionApiAsync(VMConnection connection, IFormFile formFile)
        //{
        //    ApiModel model = new ApiModel();
        //    if (formFile == null || string.IsNullOrEmpty(connection.TaxMemoryID))
        //    {
        //        model.Data = false;
        //        model.Message = MessageCode.StatusCode.TryGetValue(400, out string messager) == true
        //            ? model.Message = messager
        //            : model.Message = "کد وضعیت نامعتبر است.";
        //        model.Status = 400;
        //        model.Success = false;
        //        return model;
        //    }
        //    else
        //    {
        //        try
        //        {




        //            if (formFile != null && formFile.Length > 0)// چک کردن اینکه فایل خالی نباشد  
        //            {


        //                var hashname = SecurityHelper.EncryptString(formFile.FileName);
        //                // ساخت نام فایل منحصربه‌فرد  
        //                string filename = hashname + Path.GetExtension(formFile.FileName);
        //                string uploadDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/File/Moadian");

        //                // اگر دایرکتوری وجود ندارد، آن را بسازید  
        //                if (!Directory.Exists(uploadDir))
        //                {
        //                    Directory.CreateDirectory(uploadDir);
        //                }

        //                // مسیر کامل فایل  
        //                string filePath = Path.Combine(uploadDir, filename);

        //                try
        //                {
        //                    using (FileStream stream = new FileStream(filePath, FileMode.Create))
        //                    {
        //                        await formFile.CopyToAsync(stream);
        //                    }
        //                }
        //                catch (Exception ex)
        //                {
        //                    // مدیریت خطا  
        //                    Console.WriteLine($"Error saving file: {ex.Message}");
        //                    // می‌توانید اینجا تصمیم بگیرید که چه اقدامی انجام دهید، مثلاًبری برگرداندن خطا  
        //                }
        //                var connectiondelete =await context.ConnectInfos.Where(c => c.CpInfo == connection.cpinfoid).ToListAsync();
        //             foreach(var item in connectiondelete)
        //                {
        //                    item.IsDelete = true;
        //                }
        //                ConnectInfo connectInfo = new ConnectInfo()
        //                {
        //                    PrivateKeyPath = filename,
        //                    TaxMemoryID = connection.TaxMemoryID,
        //                    CpInfo=connection.cpinfoid,
        //                    WebServiceAddress = connection.WebServiceAddress
        //                };
        //                context.ConnectInfos.Add(connectInfo);

        //                await context.SaveChangesAsync();
        //                model.Data = connection;
        //                model.Status = 201;
        //                model.SematId = connectInfo.Id;
        //                model.Message = MessageCode.StatusCode.TryGetValue(201, out string messager) == true
        //                    ? model.Message = messager
        //                    : model.Message = "کد وضعیت نامعتبر است.";
        //                model.Success = true;

        //                return model;
        //            }
        //            else
        //            {
        //                model.Data = false;
        //                model.Message = MessageCode.StatusCode.TryGetValue(400, out string messager) == true
        //                    ? model.Message = messager
        //                    : model.Message = "کد وضعیت نامعتبر است.";
        //                model.Status = 400;
        //                model.Success = false;
        //                return model;
        //            }


        //        }
        //        catch (Exception ex)
        //        {
        //            model.Data = false;
        //            model.Message = MessageCode.StatusCode.TryGetValue(500, out string messager) == true
        //                ? model.Message = messager
        //                : model.Message = "کد وضعیت نامعتبر است.";
        //            model.Status = 500;
        //            model.Success = false;
        //            return model;
        //        }
        //    }
        //}

        public async Task<ApiModel> GetConnectionApiAsync(int cpinfoId)
        {
            if (await GuardCpInfoAsync(cpinfoId) is { } guard) return guard;
            ApiModel model = new ApiModel();
            try
            {

                VMConnection vMconnect = new VMConnection();

                var connect = await context.ConnectInfos.FirstOrDefaultAsync(c => c.CpInfo == cpinfoId && !c.IsDelete);

                if (connect == null)
                    return new ApiModel { Success = true, Status = 200, Data = null }; // اتصال ثبت‌شده ندارد (وضعیت طبیعی، نه خطا)

                var fileDetail = new FileDetail(); // لیست مجزا برای نگهداری جزئیات


                string decryptedFileName = null;
                byte[] fileContent = null; // برای نگهداری محتوای فایل

                if (!string.IsNullOrEmpty(connect.PrivateKeyPath))
                {
                    decryptedFileName = SecurityHelper.DecryptString(connect.PrivateKeyPath);

                    // مسیر فایل — wwwroot/uploads/{نام شرکت}، با fallback به wwwroot/File/Moadian قدیمی
                    var fullFilePath = await ResolveExistingKeyPathAsync(cpinfoId, connect.PrivateKeyPath);

                    // بررسی وجود فایل و خواندن محتوا
                    if (System.IO.File.Exists(fullFilePath))
                    {
                        fileContent = await System.IO.File.ReadAllBytesAsync(fullFilePath);
                    }
                }


                fileDetail.FileName = connect.PrivateKeyPath; // نام هش شده برای شناسایی در سرور
                fileDetail.DisplayName = decryptedFileName; // نام اصلی برای نمایش به کاربر
                fileDetail.FileContent = fileContent; // محتوای باینری فایل





                vMconnect.TaxMemoryID = connect.TaxMemoryID;
                vMconnect.WebServiceAddress = connect.WebServiceAddress;
                vMconnect.FilePrivatekey = fileDetail;

                model.Data = vMconnect;
                model.Status = 200;
                model.Message = MessageCode.StatusCode.TryGetValue(200, out string messager) == true
                    ? model.Message = messager
                    : model.Message = "کد وضعیت نامعتبر است.";
                model.Success = true;
                return model;

            }
            catch (Exception)
            {

                model.Data = null;
                model.Status = 500;
                model.Message = MessageCode.StatusCode.TryGetValue(500, out string messager) == true
                    ? model.Message = messager
                    : model.Message = "کد وضعیت نامعتبر است.";
                model.Success = false;

                return model;
            }
        }

        public async Task<ApiModel> EslahiInvoiceAsync(InvoiceVM modeldata)
        {
            if (await GuardCpInfoAsync(modeldata.CPInfoId ?? 0) is { } guard) return guard;
            ApiModel model = new ApiModel();
            if (modeldata.PaymentType == "" || (!IsNoBuyerDisclosureType(SafeInvoiceType(TypePatternToCode(modeldata.TypePattern))) && (modeldata.CustomerId == null || modeldata.CustomerId == 0)) || modeldata.StatusSentToSystem == "" ||
            modeldata.InvoiceItems == null)
            {
                model.Data = null;
                model.Status = 400;
                model.Message = MessageCode.StatusCode.TryGetValue(400, out string messager) == true
                    ? model.Message = messager
                    : model.Message = "کد وضعیت نامعتبر است.";
                model.Success = false;
                return model;
            }
            else
            {

                try
                {



                    List<InvoiceItem> list = new List<InvoiceItem>();
                    var conection = ConnectInfo((int)modeldata.CPInfoId);
                    //var isconnect = await IsConnectAsync(conection.TaxMemoryID, conection.PrivateKeyPath, conection.WebServiceAddress);


                    if (await IsConnectApi((int)modeldata.CPInfoId))
                    {
                        ///ثبت صورتحساب اصلاحی

                        var invoice = new Invoice()
                        {
                            CustomerId = IsNoBuyerDisclosureType(SafeInvoiceType(TypePatternToCode(modeldata.TypePattern))) ? null : modeldata.CustomerId,
                            // DateOrder = DateTime.Now.ToString(),
                            DateOrder = (DateOnly)modeldata.DateOrder,
                            TypePattern = TypePatternToCode(modeldata.TypePattern),
                            PaymentType = modeldata.PaymentType,
                            PriceNaghd=modeldata.PriceNaghd,
                            PriceNesye=modeldata.PriceNesye,
                            Description = modeldata.Description,
                            StatusSentToSystem = "1",///"ارسال شده در انتظار واکنش"
                            ShowButtons = true,
                            CpInfo = modeldata.CPInfoId,
                            Subject = "اصلاحی"
                        };
                        // اصلاحی باید به همان سال مالی صورتحساب اصلی وابسته باشد؛ وگرنه بارگذاری جزئیات (GetInvoiceByIdAsync) با خطای ۵۰۰ شکست می‌خورد
                        invoice.FiscalYearId = context.Invoices.AsNoTracking().FirstOrDefault(c => c.Id == modeldata.Id)?.FiscalYearId;
                        await context.AddAsync(invoice);
                        await context.SaveChangesAsync();

                        if (modeldata.InvoiceItems.Count > 0)
                        {
                            foreach (var item in modeldata.InvoiceItems)
                            {
                                InvoiceItem invoiceItem2 = new InvoiceItem()
                                {
                                    InvoiceId = invoice.Id,
                                    GoodsOrServicesId = (int)item.GoodsOrServicesId,
                                    UniqueIdentifier = item.UniqueIdentifier,
                                    Count = (decimal)item.Count,
                              GoodsOrServicesDescripton=item.DescriptionKala    ,
                                    PriceUnit = (int)item.PriceUnit,
                                    ValueAddedPercentage = Convert.ToInt32(item.ValueAddedPercentage),
                                    Discount = (decimal)item.Discount,
                                
                                };
                                list.Add(invoiceItem2);


                            }
                            await context.InvoiceItems.AddRangeAsync(list);
                            await context.SaveChangesAsync();
                        }
                        //تولید شماره مالیاتی یکتا برای صورتحساب

                        DateTimeOffset now = new DateTimeOffset(DateTime.Now);
                        string taxId = TaxApiService.Instance.TaxIdGenerator.GenerateTaxId(
                        conection.TaxMemoryID,
                        invoice.Id, now.DateTime);
                        string inno = invoice.Id.ToString("X").PadLeft(10, '0');
                        long indatim = now.ToUnixTimeMilliseconds();

                        var invoiceItem = (from ii in context.InvoiceItems
                                           join g in context.GoodsOrServices on ii.GoodsOrServicesId equals g.Id
                                           join um in context.UnitOfMeasurements on g.UnitOfMeasurement equals um.Name
                                           where ii.InvoiceId == invoice.Id
                                           select new
                                           {
                                               ii.InvoiceId,
                                               ii.GoodsOrServicesId,
                                               ii.UniqueIdentifier,
                                               ii.PriceUnit,
                                               ii.Count,
                                               ii.GoodsOrServicesDescripton,
                                               ii.Discount,
                                               ii.ValueAddedPercentage,
                                               g.Name,
                                               g.UnitOfMeasurement,
                                               um.Code
                                           }).ToList();

                        int pamentType = modeldata.PaymentType == "نقدی" ? 1 : modeldata.PaymentType == "نسیه" ? 2 : 3;
                        var typeSettleErr = ValidateSettlementForType(invoice.TypePattern, pamentType);
                        if (typeSettleErr is not null)
                            return new ApiModel { Data = null, Status = 400, Success = false, Message = typeSettleErr };

                        var customer = context.Customers.Where(c => c.Id == invoice.CustomerId).FirstOrDefault();
                        var cpInfo = context.CPInfos.Where(c => c.Id == invoice.CpInfo).FirstOrDefault();


                        var bodyItems = invoiceItem.Select(item =>
                        {
                            return new InvoiceBodyDto
                            {
                                Sstid = item.UniqueIdentifier,//شناسه کالا/خدمت
                                                              // Sstt = item.Name,
                                Sstt = item.Name + (item.GoodsOrServicesDescripton != null ? " " + item.GoodsOrServicesDescripton : ""),
                                Mu = item.Code,      // کد واحد معتبر
                                Am = Math.Truncate(item.Count),
                                //Cop = item.PriceNaghd,//مبلغ نقدی

                                Fee = Math.Truncate(item.PriceUnit),

                                Prdis = Math.Truncate(item.Count * item.PriceUnit),

                                Dis = Math.Truncate(item.Discount),

                                Adis = Math.Truncate(item.Count * item.PriceUnit - item.Discount),
                                Vra = item.ValueAddedPercentage,

                                Vam = Math.Truncate((item.Count * item.PriceUnit - item.Discount) / 100 * item.ValueAddedPercentage),

                                Tsstam = Math.Truncate((item.Count * item.PriceUnit - item.Discount) + ((item.Count * item.PriceUnit - item.Discount) / 100 * item.ValueAddedPercentage)),
                                //Sstid = item.UniqueIdentifier,//شناسه کالا/خدمت
                                //Sstt = item.Name,
                                //Mu = item.Code,      // کد واحد معتبر
                                //Am = item.Count,
                                //Fee = item.PriceUnit,
                                //Prdis = item.Count * item.PriceUnit,
                                //Dis = item.Discount,
                                //Adis = item.Count * item.PriceUnit - item.Discount,
                                //Vra = item.ValueAddedPercentage,
                                //Vam = (item.Count * item.PriceUnit - item.Discount) / 100 * item.ValueAddedPercentage,
                                //Tsstam = (item.Count * item.PriceUnit - item.Discount) + ((item.Count * item.PriceUnit - item.Discount) / 100 * item.ValueAddedPercentage)
                            };
                        }).ToList();


                        var header = new InvoiceHeaderDto
                        {
                            Irtaxid = modeldata.TaxNumber,
                            Taxid = taxId,
                            Inno = inno,
                            Indatim = indatim,
                            Inty = SafeInvoiceType(invoice.TypePattern), // نوع صورتحساب (سرادم): ۱/۲/۳
                            Inp = 1, // الگوی فروش عادی
                            Ins = 2,
                            Tins = cpInfo.EconomicNumber,
                            Tinb = IsNoBuyerDisclosureType(SafeInvoiceType(invoice.TypePattern)) ? null : customer.EconomicNumber,
                                Bid = IsNoBuyerDisclosureType(SafeInvoiceType(invoice.TypePattern)) ? null : customer.NationalID,
                                Tob = IsNoBuyerDisclosureType(SafeInvoiceType(invoice.TypePattern)) ? null : (customer.Type == "حقیقی" ? 1 : 2),
                                Bpc = IsNoBuyerDisclosureType(SafeInvoiceType(invoice.TypePattern)) ? null : customer.PostalCode,
                            Tprdis = bodyItems.Sum(x => x.Prdis),
                            Tdis = bodyItems.Sum(x => x.Dis),
                            Tadis = bodyItems.Sum(x => x.Adis),
                            Tvam = bodyItems.Sum(x => x.Vam),
                            Todam = 0,
                            Tbill = bodyItems.Sum(x => x.Tsstam),
                            Setm = pamentType

                        };

                        InvoiceDto invoice2 = new InvoiceDto
                        {
                            Header = header,
                            Body = bodyItems
                        };


                        List<InvoiceDto> invoiceList = new List<InvoiceDto>();
                        invoiceList.Add(invoice2);


                        var response = TaxApiService.Instance.TaxApis.SendInvoices(
                      invoiceList,
                      null);
                        var packetResponse = response?.Body?.Result?.FirstOrDefault();
                        if (packetResponse is null)
                        {
                            var sendErr = ExtractSdkErrors(response);
                            return new ApiModel
                            {
                                Data = null,
                                Status = 500,
                                Success = false,
                                Message = "ارسال صورتحساب به سامانهٔ مودیان ناموفق بود و آن را «ارسال شده» علامت نزنیم." +
                                    (string.IsNullOrEmpty(sendErr) ? " پاسخی از سامانه برنگشت." : $"\nفهرست خطای برگشتی سامانه: {sendErr}")
                            };
                        }
                        var uid = packetResponse.Uid;
                        var referenceId = packetResponse.ReferenceNumber;

                        invoice.ShowButtons = false;

                        var newInvoiceForEdit = context.Invoices.Where(c => c.Id == invoice.Id).FirstOrDefault();
                        newInvoiceForEdit.NewTaxNumber = taxId;
                        newInvoiceForEdit.TaxNumber = modeldata.TaxNumber;
                        newInvoiceForEdit.ReferenceId = referenceId;

                        await context.SaveChangesAsync();
                        model.Data = modeldata;
                        model.Status = 201;
                        model.Message = MessageCode.StatusCode.TryGetValue(201, out string messager2) == true
                            ? model.Message = messager2
                            : model.Message = "کد وضعیت نامعتبر است.";
                        model.Success = true;

                        return model;


                    }
                    model.Data = modeldata;
                    model.Status = 500;
                    model.Message = MessageCode.StatusCode.TryGetValue(500, out string messager1) == true
                        ? model.Message = messager1
                        : model.Message = "کد وضعیت نامعتبر است.";
                    model.Success = true;

                    return model;








                }
                catch (Exception)
                {
                    model.Data = null;
                    model.Status = 500;
                    model.Message = MessageCode.StatusCode.TryGetValue(500, out string messager) == true
                        ? model.Message = messager
                        : model.Message = "کد وضعیت نامعتبر است.";
                    model.Success = false;

                    return model;
                }
            }
        }

        public async Task<ApiModel> EbtalInvoiceAsync(InvoiceVM modeldata)
        {
            if (await GuardCpInfoAsync(modeldata.CPInfoId ?? 0) is { } guard) return guard;
            ApiModel model = new ApiModel();
            if (modeldata.PaymentType == "" || (!IsNoBuyerDisclosureType(SafeInvoiceType(TypePatternToCode(modeldata.TypePattern))) && (modeldata.CustomerId == null || modeldata.CustomerId == 0)) || modeldata.StatusSentToSystem == "" ||
            modeldata.InvoiceItems == null)
            {
                model.Data = null;
                model.Status = 400;
                model.Message = MessageCode.StatusCode.TryGetValue(400, out string messager) == true
                    ? model.Message = messager
                    : model.Message = "کد وضعیت نامعتبر است.";
                model.Success = false;
                return model;
            }
            else
            {

                try
                {

                    List<InvoiceItem> list = new List<InvoiceItem>();
                    var conection = ConnectInfo((int)modeldata.CPInfoId);
                    //var isconnect = await IsConnectAsync(conection.TaxMemoryID, conection.PrivateKeyPath, conection.WebServiceAddress);


                    if (await IsConnectApi((int)modeldata.CPInfoId))
                    {

                        var invoice = new Invoice()
                        {
                            CustomerId = IsNoBuyerDisclosureType(SafeInvoiceType(TypePatternToCode(modeldata.TypePattern))) ? null : modeldata.CustomerId,
                            DateOrder = DateOnly.FromDateTime(DateTime.Now),
                            PaymentType = modeldata.PaymentType,
                            Description = modeldata.Description,
                            CpInfo = modeldata.CPInfoId,
                            TypePattern = TypePatternToCode(modeldata.TypePattern),
                            StatusSentToSystem = "6",//عدم نیاز به واکنش
                            ShowButtons = false,
                            Subject = "ابطالی"
                        };
                        // ابطالی به سال مالی صورتحساب مرجع وابسته باشد تا جزئیات آن بعداً خطای ۵۰۰ ندهد
                        invoice.FiscalYearId = context.Invoices.AsNoTracking().FirstOrDefault(c => c.Id == modeldata.Id)?.FiscalYearId;
                        await context.AddAsync(invoice);
                        await context.SaveChangesAsync();
                        //تولید شماره مالیاتی یکتا برای صورتحساب

                        DateTimeOffset now = new DateTimeOffset(DateTime.Now);
                        string taxId = TaxApiService.Instance.TaxIdGenerator.GenerateTaxId(
                        conection.TaxMemoryID,
                        invoice.Id, now.DateTime);
                        string inno = modeldata.Id.ToString("X").PadLeft(10, '0');
                        long indatim = now.ToUnixTimeMilliseconds();

                        var invoiceItem = (from ii in context.InvoiceItems
                                           join g in context.GoodsOrServices on ii.GoodsOrServicesId equals g.Id
                                           join um in context.UnitOfMeasurements on g.UnitOfMeasurement equals um.Name
                                           where ii.InvoiceId == modeldata.Id
                                           select new
                                           {
                                               ii.InvoiceId,
                                               ii.GoodsOrServicesId,
                                               ii.UniqueIdentifier,
                                               ii.PriceUnit,
                                               ii.Count,
                                               ii.GoodsOrServicesDescripton,
                                               ii.Discount,
                                               ii.ValueAddedPercentage,
                                               g.Name,
                                               g.UnitOfMeasurement,
                                               um.Code
                                           }).ToList();

                        int pamentType = modeldata.PaymentType == "نقدی" ? 1 : modeldata.PaymentType == "نسیه" ? 2 : 3;
                        var typeSettleErr = ValidateSettlementForType(invoice.TypePattern, pamentType);
                        if (typeSettleErr is not null)
                            return new ApiModel { Data = null, Status = 400, Success = false, Message = typeSettleErr };

                        var customer = context.Customers.Where(c => c.Id == invoice.CustomerId).FirstOrDefault();
                        var cpInfo = context.CPInfos.Where(c => c.Id == invoice.CpInfo).FirstOrDefault();


                        var bodyItems = invoiceItem.Select(item =>
                        {
                            return new InvoiceBodyDto
                            {
                                Sstid = item.UniqueIdentifier,//شناسه کالا/خدمت
                                                              // Sstt = item.Name,
                                Sstt = item.Name + (item.GoodsOrServicesDescripton != null ? " " + item.GoodsOrServicesDescripton : ""),
                                Mu = item.Code,      // کد واحد معتبر
                                Am = Math.Truncate(item.Count),
                                //Cop = item.PriceNaghd,//مبلغ نقدی

                                Fee = Math.Truncate(item.PriceUnit),

                                Prdis = Math.Truncate(item.Count * item.PriceUnit),

                                Dis = Math.Truncate(item.Discount),

                                Adis = Math.Truncate(item.Count * item.PriceUnit - item.Discount),
                                Vra = item.ValueAddedPercentage,

                                Vam = Math.Truncate((item.Count * item.PriceUnit - item.Discount) / 100 * item.ValueAddedPercentage),

                                Tsstam = Math.Truncate((item.Count * item.PriceUnit - item.Discount) + ((item.Count * item.PriceUnit - item.Discount) / 100 * item.ValueAddedPercentage)),
                                //Sstid = item.UniqueIdentifier,//شناسه کالا/خدمت
                                //Sstt = item.Name,
                                //Mu = item.Code,      // کد واحد معتبر
                                //Am = item.Count,
                                //Fee = item.PriceUnit,
                                //Prdis = item.Count * item.PriceUnit,
                                //Dis = item.Discount,
                                //Adis = item.Count * item.PriceUnit - item.Discount,
                                //Vra = item.ValueAddedPercentage,
                                //Vam = (item.Count * item.PriceUnit - item.Discount) / 100 * item.ValueAddedPercentage,
                                //Tsstam = (item.Count * item.PriceUnit - item.Discount) + ((item.Count * item.PriceUnit - item.Discount) / 100 * item.ValueAddedPercentage)
                            };
                        }).ToList();


                        var header = new InvoiceHeaderDto
                        {
                            Irtaxid = string.IsNullOrWhiteSpace(modeldata.NewTaxNumber) ? modeldata.TaxNumber : modeldata.NewTaxNumber,
                            Taxid = taxId,
                            Inno = inno,
                            Indatim = indatim,
                            Inty = SafeInvoiceType(invoice.TypePattern), // نوع صورتحساب (سرادم): ۱/۲/۳
                            Inp = 1, // الگوی فروش عادی
                            Ins = 3,
                            Tins = cpInfo.EconomicNumber,
                            Tinb = IsNoBuyerDisclosureType(SafeInvoiceType(invoice.TypePattern)) ? null : customer.EconomicNumber,
                                Bid = IsNoBuyerDisclosureType(SafeInvoiceType(invoice.TypePattern)) ? null : customer.NationalID,
                                Tob = IsNoBuyerDisclosureType(SafeInvoiceType(invoice.TypePattern)) ? null : (customer.Type == "حقیقی" ? 1 : 2),
                                Bpc = IsNoBuyerDisclosureType(SafeInvoiceType(invoice.TypePattern)) ? null : customer.PostalCode,
                            Tprdis = bodyItems.Sum(x => x.Prdis),
                            Tdis = bodyItems.Sum(x => x.Dis),
                            Tadis = bodyItems.Sum(x => x.Adis),
                            Tvam = bodyItems.Sum(x => x.Vam),
                            Todam = 0,
                            Tbill = bodyItems.Sum(x => x.Tsstam),
                            Setm = pamentType

                        };

                        InvoiceDto invoice2 = new InvoiceDto
                        {
                            Header = header,
                            Body = bodyItems
                        };


                        List<InvoiceDto> invoiceList = new List<InvoiceDto>();
                        invoiceList.Add(invoice2);


                        var response = TaxApiService.Instance.TaxApis.SendInvoices(
                      invoiceList,
                      null);
                        var packetResponse = response?.Body?.Result?.FirstOrDefault();
                        if (packetResponse is null)
                        {
                            var sendErr = ExtractSdkErrors(response);
                            return new ApiModel
                            {
                                Data = null,
                                Status = 500,
                                Success = false,
                                Message = "ارسال صورتحساب به سامانهٔ مودیان ناموفق بود و آن را «ارسال شده» علامت نزنیم." +
                                    (string.IsNullOrEmpty(sendErr) ? " پاسخی از سامانه برنگشت." : $"\nفهرست خطای برگشتی سامانه: {sendErr}")
                            };
                        }
                        var uid = packetResponse.Uid;
                        var referenceId = packetResponse.ReferenceNumber;

                        invoice.ShowButtons = false;

                        var newInvoiceForEdit = context.Invoices.Where(c => c.Id == invoice.Id).FirstOrDefault();
                        newInvoiceForEdit.NewTaxNumber = taxId;
                        newInvoiceForEdit.TaxNumber = modeldata.TaxNumber;
                        newInvoiceForEdit.ReferenceId = referenceId;
                        await context.SaveChangesAsync();

                        model.Data = modeldata;
                        model.Status = 201;
                        model.Message = MessageCode.StatusCode.TryGetValue(201, out string messager2) == true
                            ? model.Message = messager2
                            : model.Message = "کد وضعیت نامعتبر است.";
                        model.Success = true;

                        return model;
                    }
                    else
                    {
                        model.Data = modeldata;
                        model.Status = 500;
                        model.Message = MessageCode.StatusCode.TryGetValue(500, out string messager2) == true
                            ? model.Message = messager2
                            : model.Message = "کد وضعیت نامعتبر است.";
                        model.Success = true;

                        return model;
                    }


                }
                catch (Exception)
                {
                    model.Data = null;
                    model.Status = 500;
                    model.Message = MessageCode.StatusCode.TryGetValue(500, out string messager) == true
                        ? model.Message = messager
                        : model.Message = "کد وضعیت نامعتبر است.";
                    model.Success = false;

                    return model;
                }
            }
        }

        public async Task<ApiModel> InsertUnitOfMeasurementAsync(VMUnitOfMeasurement modeldata)
        {
            if (await GuardCpInfoAsync(modeldata.CpInfoId) is { } guard) return guard;
            ApiModel model = new ApiModel();
            if (string.IsNullOrEmpty(modeldata.Name) || string.IsNullOrEmpty(modeldata.Code)
            )
            {
                model.Data = null;
                model.Status = 400;
                model.Message = MessageCode.StatusCode.TryGetValue(400, out string messager) == true
                    ? model.Message = messager
                    : model.Message = "کد وضعیت نامعتبر است.";
                model.Success = false;
                return model;
            }
            else
            {
                try
                {

                    var result = new UnitOfMeasurement()
                    {
                        Code = modeldata.Code,
                        Name = modeldata.Name,
                        CpInfo = modeldata.CpInfoId,

                    };

                    await context.AddAsync(result);
                    await context.SaveChangesAsync();

                    model.Data = result;
                    model.Status = 201;
                    model.Message = MessageCode.StatusCode.TryGetValue(201, out string messager) == true
                        ? model.Message = messager
                        : model.Message = "کد وضعیت نامعتبر است.";
                    model.Success = true;

                    return model;

                }
                catch (Exception)
                {
                    model.Data = null;
                    model.Status = 500;
                    model.Message = MessageCode.StatusCode.TryGetValue(500, out string messager) == true
                        ? model.Message = messager
                        : model.Message = "کد وضعیت نامعتبر است.";
                    model.Success = false;

                    return model;
                }
            }
        }

        public async Task<ApiModel> UpdateUnitOfMeasurementAsync(VMUnitOfMeasurement modeldata)
        {
            ApiModel model = new ApiModel();
            if (string.IsNullOrEmpty(modeldata.Name) || string.IsNullOrEmpty(modeldata.Code))
            {
                model.Data = null;
                model.Status = 400;
                model.Message = MessageCode.StatusCode.TryGetValue(400, out string messager) == true
                    ? model.Message = messager
                    : model.Message = "کد وضعیت نامعتبر است.";
                model.Success = false;
                return model;
            }
            else
            {
                try
                {
                    var data = await context.UnitOfMeasurements.FindAsync(modeldata.UnitOfMeasurementId);

                    if (data is not null)
                    {
                        // واحد بین خدمات‌دهنده‌ها جابه‌جا نمی‌شود؛ مالکِ ثبت‌شده ملاک دسترسی است.
                        if (await GuardCpInfoAsync(data.CpInfo ?? 0) is { } guard) return guard;
                        data.Name = modeldata.Name;
                        data.Code = modeldata.Code;

                        await context.SaveChangesAsync();
                        model.Data = data;
                        model.Status = 201;
                        model.Message = MessageCode.StatusCode.TryGetValue(201, out string messager) == true
                            ? model.Message = messager
                            : model.Message = "کد وضعیت نامعتبر است.";
                        model.Success = true;
                    }

                    return model;

                }
                catch (Exception)
                {
                    model.Data = null;
                    model.Status = 500;
                    model.Message = MessageCode.StatusCode.TryGetValue(500, out string messager) == true
                        ? model.Message = messager
                        : model.Message = "کد وضعیت نامعتبر است.";
                    model.Success = false;

                    return model;
                }
            }
        }

        public async Task<ApiModel> GetAllUnitOfMeasurementAsync(int cpinfoId, GetResultQuery request)
        {
            if (await GuardCpInfoAsync(cpinfoId) is { } guard) return guard;
            ApiModel model = new ApiModel();

            try
            {

                var query = context.UnitOfMeasurements.Where(c => c.CpInfo == cpinfoId && !c.IsDelete).AsQueryable();


                // جستجو مدارک بر اساس نام، شماره یکتا  
                if (!string.IsNullOrEmpty(request.SearchTerm))
                {
                    query = query.Where(c => c.Name.Trim().Contains(request.SearchTerm) || c.Code.Trim().Contains(request.SearchTerm));
                }

                //Order => صعودی یا نزولی desc/asc
                //بعد از آن مرتب کن بر اساس ستونی که کاربر می دهد SortColumn
                if (request.SortColumn != null)
                {
                    if (request.SortOrder?.ToLower() == "desc")
                    {
                        query = request.SortColumn.ToLower() switch
                        {
                            "Code" => query.OrderByDescending(x => x.Code),



                            _ => query.OrderByDescending(x => x.Id)
                        };
                    }
                    else
                    {
                        query = request.SortColumn.ToLower() switch
                        {
                            "Code" => query.OrderBy(x => x.Code),



                            _ => query.OrderBy(x => x.Id)
                        };
                    }
                }
                else
                {
                    query = query.OrderBy(x => x.Id);
                }
                var result = query.Select(c => new VMUnitOfMeasurement()
                {
                    UnitOfMeasurementId = c.Id,
                    CpInfoId = cpinfoId,
                    Name = c.Name,
                    Code = c.Code,


                });
                var data = await PagedList<VMUnitOfMeasurement>.CreateAsync(result, (int)request.Page, (int)request.PageSize);


                model.Data = data;
                model.Status = 200;
                model.Success = true;
                model.Message = MessageCode.StatusCode.TryGetValue(200, out string messager) == true
                    ? model.Message = messager
                    : model.Message = "کد وضعیت نامعتبر است.";
                return model;
            }
            catch (Exception exception)
            {
                model.Data = null;
                model.Message = MessageCode.StatusCode.TryGetValue(500, out string messager) == true
                    ? model.Message = messager
                    : model.Message = exception.Message;
                model.Status = 500;
                model.Success = false;
                return model;
            }
        }

        public async Task<ApiModel> GetUnitOfMeasurementByIdAsync(int Id)
        {
            ApiModel model = new ApiModel();
            var result = await context.UnitOfMeasurements.FirstOrDefaultAsync(c => c.Id == Id);

            if (result is not null)
            {
                if (await GuardCpInfoAsync(result.CpInfo ?? 0) is { } guard) return guard;
                var respose = new VMUnitOfMeasurement
                {
                    Name = result.Name,
                    Code = result.Code,
                    CpInfoId = result.CpInfo ?? 0,

                };
                model.Data = respose;
                model.Status = 200;
                model.Message = MessageCode.StatusCode.TryGetValue(200, out string messager) == true
                    ? model.Message = messager
                    : model.Message = "کد وضعیت نامعتبر است.";
                model.Success = true;
                return model;
            }




            else
            {
                model.Data = null;
                model.Message = MessageCode.StatusCode.TryGetValue(404, out string messager) == true
                    ? model.Message = messager
                    : model.Message = "کد وضعیت نامعتبر است.";
                model.Status = 404;
                model.Success = false;

                return model;
            }

        }

        public async Task<ApiModel> DeleteUnitOfMeasurementByIdAsync(int Id)
        {
            ApiModel model = new ApiModel();
            if (Id == 0)
            {
                model.Data = null;
                model.Status = 400;
                model.Message = MessageCode.StatusCode.TryGetValue(400, out string messager) == true
                    ? model.Message = messager
                    : model.Message = "کد وضعیت نامعتبر است.";
                model.Success = false;
                return model;
            }
            else
            {
                try
                {
                    var data = await context.UnitOfMeasurements.FirstOrDefaultAsync(c => c.Id == Id);
                    if (data is not null && await GuardCpInfoAsync(data.CpInfo ?? 0) is { } guard) return guard;
                    var isuse = context.GoodsOrServices.Any(c => c.Id == Id);
                    if (isuse)
                    {
                        model.Data = false;
                        model.Status = 409;
                        model.Message = MessageCode.StatusCode.TryGetValue(409, out string messager) == true
                            ? model.Message = messager
                            : model.Message = "کد وضعیت نامعتبر است.";
                        model.Success = false;
                        return model;
                    }
                    if (data is not null)
                    {
                        data.IsDelete = true;
                        await context.SaveChangesAsync();
                        model.Data = true;
                        model.Status = 201;
                        model.Message = MessageCode.StatusCode.TryGetValue(201, out string messager) == true
                            ? model.Message = messager
                            : model.Message = "کد وضعیت نامعتبر است.";
                        model.Success = true;
                    }
                    else
                    {
                        model.Data = false;
                        model.Status = 500;
                        model.Message = MessageCode.StatusCode.TryGetValue(500, out string messager) == true
                            ? model.Message = messager
                            : model.Message = "کد وضعیت نامعتبر است.";
                        model.Success = false;
                    }

                    return model;

                }
                catch (Exception)
                {
                    model.Data = false;
                    model.Status = 500;
                    model.Message = MessageCode.StatusCode.TryGetValue(500, out string messager) == true
                        ? model.Message = messager
                        : model.Message = "کد وضعیت نامعتبر است.";
                    model.Success = false;

                    return model;
                }
            }
        }
        private async Task<ApiModel> UpdateInvoiceAsync(VmInvoiceItems.InvoiceVM modeldata)
        {
            ApiModel model = new ApiModel();
            
            if (modeldata.PaymentType == "" || (!IsNoBuyerDisclosureType(SafeInvoiceType(TypePatternToCode(modeldata.TypePattern))) && (modeldata.CustomerId == null || modeldata.CustomerId == 0)) || modeldata.StatusSentToSystem == "" ||
            modeldata.InvoiceItems == null)
            {
                model.Data = null;
                model.Status = 400;
                model.Message = MessageCode.StatusCode.TryGetValue(400, out string messagers) == true
                    ? model.Message = messagers
                    : model.Message = "کد وضعیت نامعتبر است.";
                model.Success = false;
                return model;
            }
            List<InvoiceItem> list = new List<InvoiceItem>();
            ////ویرایش صورت حساب ارسال نشده
            var invoiceForEdit = context.Invoices.Where(c => c.Id == modeldata.Id).FirstOrDefault();
            invoiceForEdit.DateOrder = (DateOnly)modeldata.DateOrder;
            invoiceForEdit.CustomerId = IsNoBuyerDisclosureType(SafeInvoiceType(TypePatternToCode(modeldata.TypePattern))) ? null : modeldata.CustomerId;
            invoiceForEdit.PaymentType = modeldata.PaymentType;
            invoiceForEdit.Description = modeldata.Description;
            if (invoiceForEdit.PaymentType == "نقدی")
            {
                invoiceForEdit.PriceNesye = null;
                invoiceForEdit.PriceNaghd = modeldata.PriceNaghd;
            }
            else if(invoiceForEdit.PaymentType == "نسیه")
            {
                invoiceForEdit.PriceNesye = modeldata.PriceNesye;
                invoiceForEdit.PriceNaghd = null;
            }
            else
            {
                invoiceForEdit.PriceNesye = modeldata.PriceNesye;
                invoiceForEdit.PriceNaghd = modeldata.PriceNaghd;
            }
         
            invoiceForEdit.TypePattern = TypePatternToCode(modeldata.TypePattern);

            context.InvoiceItems.RemoveRange(context.InvoiceItems.Where(x => x.InvoiceId == modeldata.Id));

            if (modeldata.InvoiceItems.Count > 0)
            {
                foreach (var item in modeldata.InvoiceItems)
                {
                    InvoiceItem invoiceItem2 = new InvoiceItem()
                    {
                        InvoiceId = modeldata.Id,
                        GoodsOrServicesId = (int)item.GoodsOrServicesId,
                        UniqueIdentifier = item.UniqueIdentifier,
                        Count = (decimal)item.Count,
                        GoodsOrServicesDescripton=item.DescriptionKala,
                        //PriceNesye = Convert.ToDecimal(modeldata.NesyeText),
                        //PriceNaghd = Convert.ToDecimal(modeldata.NaghdText),
                        PriceUnit = (int)item.PriceUnit,
                        ValueAddedPercentage = Convert.ToInt32(item.ValueAddedPercentage),
                        Discount = (decimal)item.Discount,
                    };
                    list.Add(invoiceItem2);


                }
                try
                {
                    await context.InvoiceItems.AddRangeAsync(list);
                    await context.SaveChangesAsync();
                }
                catch (Exception ex)
                {

                    Console.WriteLine(ex.Message);
                }

             
            }
            model.Data = modeldata;
            model.Status = 201;
            model.Message = MessageCode.StatusCode.TryGetValue(201, out string messager) == true
                ? model.Message = messager
                : model.Message = "کد وضعیت نامعتبر است.";
            model.Success = true;

            return model;
        }
        private async Task<ApiModel> CreateInvoiceAsync(VmInvoiceItems.InvoiceVM modeldata)
        {
            ApiModel model = new ApiModel();

            // نرمال‌سازی نوع صورتحساب در ورودی: هر مقداری که از کلاینت برسد («نوع دوم»، «2»، با نیم‌فاصله و…)
            // به یکی از سه مقدار قطعی تبدیل می‌شود تا همهٔ تصمیم‌های بعدی (از جمله رد/قبول مشتری) یکی باشند.
            var rawType = modeldata.TypePattern;
            var typeCode = TypePatternToCode(rawType);
            modeldata.TypePattern = typeCode == 3 ? "نوع سوم" : typeCode == 2 ? "نوع دوم" : "نوع اول";
            Console.WriteLine($"[Moadian] CreateInvoice typePattern='{rawType}' code={typeCode} customerId={modeldata.CustomerId} cpinfo={modeldata.CPInfoId}");

            // Validation اولیه
            if (modeldata.PaymentType == "" || (!IsNoBuyerDisclosureType(SafeInvoiceType(TypePatternToCode(modeldata.TypePattern))) && (modeldata.CustomerId == null || modeldata.CustomerId == 0)) || modeldata.StatusSentToSystem == "" ||
                modeldata.InvoiceItems == null || !modeldata.InvoiceItems.Any())
            {
                return CreateErrorResponse(400);
            }

            // واکشی کالاها با Dictionary برای دسترسی سریعتر
            var goodsIds = modeldata.InvoiceItems
                .Select(i => i.UniqueIdentifier)
                .Distinct()
                .ToList();
            try
            {
                var goodsDictionary = await context.GoodsOrServices
               .Where(g => goodsIds.Contains(g.UniqueIdentifier))
               .ToDictionaryAsync(
                   g => g.UniqueIdentifier,  // Key
                   g => g.Id                  // Value (کلید اصلی)
               );
                // بررسی missing IDs
                var missingIds = goodsIds.Where(id => !goodsDictionary.ContainsKey(id)).ToList();

                if (missingIds.Any())
                {
                    model.Success = false;
                    model.Status = 400;
                    model.Message = $"کالا(های) با شناسه {string.Join(", ", missingIds)} در سیستم یافت نشد.";
                    return model;
                }
                // چک مشتری — برای نوع ۲/۳ (بدون خریدار) نیازی به مشتری نیست
                if (!IsNoBuyerDisclosureType(SafeInvoiceType(TypePatternToCode(modeldata.TypePattern))))
                {
                    var customerExists = await context.Customers.AnyAsync(c => c.Id == modeldata.CustomerId);
                    if (!customerExists)
                    {
                        model.Success = false;
                        model.Status = 400;
                        model.Message = "مشتری موردنظر یافت نشد.";
                        return model;
                    }
                }

                try
                {
                    // ایجاد فاکتور — اگر شماره ارسال نشده، شمارهٔ بعدیِ همان شرکت تخصیص می‌یابد
                    int nextNumber = modeldata.NumberInvoice is > 0
                        ? modeldata.NumberInvoice.Value
                        : await NextInvoiceNoCoreAsync(modeldata.CPInfoId);
                    var invoice = new Invoice()
                    {
                        CustomerId = IsNoBuyerDisclosureType(SafeInvoiceType(TypePatternToCode(modeldata.TypePattern))) ? null : modeldata.CustomerId,
                        CpInfo = modeldata.CPInfoId,
                        InvoiceNumber = nextNumber,
                        FiscalYearId = Convert.ToInt32(modeldata.FiscalYearId),
                        DateOrder = (DateOnly)modeldata.DateOrder,
                        TypePattern = TypePatternToCode(modeldata.TypePattern),
                        PaymentType = modeldata.PaymentType,
                        PriceNaghd = modeldata.PriceNaghd,
                        PriceNesye = modeldata.PriceNesye,
                        Description = modeldata.Description,
                        StatusSentToSystem = "0",
                        ShowButtons = true,
                        Subject = "اصلی"
                    };

                    await context.AddAsync(invoice);
                    await context.SaveChangesAsync();

                    // ایجاد آیتم‌های فاکتور
                    var invoiceItems = modeldata.InvoiceItems.Select(item => new InvoiceItem()
                    {
                        InvoiceId = invoice.Id,
                        GoodsOrServicesId = goodsDictionary[item.UniqueIdentifier],  // گرفتن کلید اصلی از Dictionary
                        UniqueIdentifier = item.UniqueIdentifier,
                        Count = (decimal)item.Count,
                        GoodsOrServicesDescripton = item.DescriptionKala,
                        PriceUnit = (int)item.PriceUnit,
                        ValueAddedPercentage = Convert.ToInt32(item.ValueAddedPercentage),
                        Discount = (decimal)item.Discount,
                    }).ToList();

                    await context.InvoiceItems.AddRangeAsync(invoiceItems);
                    await context.SaveChangesAsync();

                    // پاسخ غنی‌شده: Id و شمارهٔ واقعی صورتحساب برای ناوبری کلاینت
                    modeldata.Id = invoice.Id;
                    modeldata.NumberInvoice = invoice.InvoiceNumber;
                    modeldata.DateOrder = invoice.DateOrder;
                    modeldata.Subject = invoice.Subject;
                    modeldata.StatusSentToSystem = invoice.StatusSentToSystem;
                    modeldata.StatusSentToSystemInt = invoice.StatusSentToSystem;
                    modeldata.ShowButtons = true;

                    model.Data = modeldata;
                    model.Status = 201;
                    model.Message = MessageCode.StatusCode.TryGetValue(201, out string messager) == true
                        ? model.Message = messager
                        : model.Message = "کد وضعیت نامعتبر است.";
                    model.Success = true;

                    return model;
                }
                catch (Exception e)
                {
                    // لاگ کردن خطا
                    Console.WriteLine($"Error in CreateInvoiceAsync: {e.Message}");
                    Console.WriteLine($"Stack Trace: {e.StackTrace}");

                    return CreateErrorResponse(500);
                }
            }
            catch (Exception)
            {

                throw;
            }
           

           

         
        }

    
   
        //    private async Task<ApiModel> CreateInvoiceAsync(VmInvoiceItems.InvoiceVM modeldata)
        //    {
        //        ApiModel model = new ApiModel();
        //        if (modeldata.PaymentType == "" || (!IsNoBuyerDisclosureType(SafeInvoiceType(TypePatternToCode(modeldata.TypePattern))) && (modeldata.CustomerId == null || modeldata.CustomerId == 0)) || modeldata.StatusSentToSystem == "" ||
        //        modeldata.InvoiceItems == null)
        //        {
        //            model.Data = null;
        //            model.Status = 400;
        //            model.Message = MessageCode.StatusCode.TryGetValue(400, out string messager) == true
        //                ? model.Message = messager
        //                : model.Message = "کد وضعیت نامعتبر است.";
        //            model.Success = false;
        //            return model;
        //        }
        //        // 2. Validation وجود موجودیت‌ها در دیتابیس (Business validation)
        //        var goodsIds = modeldata.InvoiceItems
        //.Select(i => i.UniqueIdentifier)
        //.Distinct()
        //.ToList();

        //        var missingIds = new List<string>();

        //        foreach (var goodsId in goodsIds)
        //        {
        //            var exists = await context.GoodsOrServices
        //                .AnyAsync(g => g.UniqueIdentifier == goodsId);

        //            if (!exists)
        //            {
        //                missingIds.Add(goodsId);
        //            }
        //        }

        //        if (missingIds.Any())
        //        {
        //            model.Success = false;
        //            model.Status = 400;
        //            model.Message = $"کالا(های) با شناسه {string.Join(", ", missingIds)} در سیستم یافت نشد.";
        //            return model;
        //        }

        //        if (missingIds.Any())
        //        {
        //            model.Success = false;
        //            model.Status = 400;
        //            model.Message = $"کالا(های) با شناسه {string.Join(",", missingIds)} در سیستم یافت نشد.";
        //            return model;
        //        }

        //        // چک وجود مشتری هم اینجا اضافه کن
        //        var customerExists = await context.Customers.AnyAsync(c => c.Id == modeldata.CustomerId);
        //        if (!customerExists)
        //        {
        //            model.Success = false;
        //            model.Status = 400;
        //            model.Message = "مشتری موردنظر یافت نشد.";
        //            return model;
        //        }
        //        else
        //        {

        //            try
        //            {

        //                var invoice = new Invoice()
        //                {
        //                    CustomerId = IsNoBuyerDisclosureType(SafeInvoiceType(TypePatternToCode(modeldata.TypePattern))) ? null : modeldata.CustomerId,
        //                    CpInfo = modeldata.CPInfoId,
        //                    InvoiceNumber = modeldata.NumberInvoice,
        //                    FiscalYearId = Convert.ToInt32(modeldata.FiscalYearId),
        //                    DateOrder = (DateOnly)modeldata.DateOrder,
        //                    TypePattern = TypePatternToCode(modeldata.TypePattern),
        //                    PaymentType = modeldata.PaymentType,
        //                    PriceNaghd = modeldata.PriceNaghd,
        //                    PriceNesye = modeldata.PriceNesye,
        //                    Description = modeldata.Description,
        //                    StatusSentToSystem = "0",///"ارسال نشده"
        //                    ShowButtons = true,
        //                    Subject = "اصلی"
        //                };

        //                await context.AddAsync(invoice);
        //                await context.SaveChangesAsync();
        //                List<InvoiceItem> list = new List<InvoiceItem>();
        //                if (modeldata.InvoiceItems.Count > 0)
        //                {
        //                    foreach (var item in modeldata.InvoiceItems)
        //                    {
        //                        InvoiceItem invoiceItem2 = new InvoiceItem()
        //                        {
        //                            InvoiceId = invoice.Id,
        //                            GoodsOrServicesId = (int)item.GoodsOrServicesId,
        //                            UniqueIdentifier = item.UniqueIdentifier,
        //                            Count = (decimal)item.Count,
        //                            GoodsOrServicesDescripton=item.DescriptionKala,
        //                            //PriceNesye = Convert.ToDecimal(modeldata.NesyeText),
        //                            //PriceNaghd = Convert.ToDecimal(modeldata.NaghdText),
        //                            PriceUnit = (int)item.PriceUnit,
        //                            ValueAddedPercentage = Convert.ToInt32(item.ValueAddedPercentage),
        //                            Discount = (decimal)item.Discount,
        //                        };
        //                        list.Add(invoiceItem2);


        //                    }
        //                    await context.InvoiceItems.AddRangeAsync(list);
        //                    await context.SaveChangesAsync();
        //                }


        //                model.Data = modeldata;
        //                model.Status = 201;
        //                model.Message = MessageCode.StatusCode.TryGetValue(201, out string messager) == true
        //                    ? model.Message = messager
        //                    : model.Message = "کد وضعیت نامعتبر است.";
        //                model.Success = true;

        //                return model;



        //            }
        //            catch (Exception e)
        //            {
        //                model.Data = null;
        //                model.Status = 500;
        //                model.Message = MessageCode.StatusCode.TryGetValue(500, out string messager) == true
        //                    ? model.Message = messager
        //                    : model.Message = "کد وضعیت نامعتبر است.";
        //                model.Success = false;

        //                return model;
        //            }
        //        }
        //    }

        public async Task<ApiModel> DeleteInvoiceByIdAsync(int InvoiceId)
        {
            ApiModel model = new ApiModel();
            var result = await context.Invoices.Include(i => i.invoiceItem).FirstOrDefaultAsync(c => c.Id == InvoiceId && !c.IsDelete);

            if (result is not null)

            {
                if (result.StatusSentToSystem != "0" || !result.ShowButtons)
                {
                    return new ApiModel
                    {
                        Data = false,
                        Status = 400,
                        Success = false,
                        Message = "فقط صورتحساب ارسال‌نشده قابل حذف است."
                    };
                }
                if (await GuardCpInfoAsync(result.CpInfo ?? 0) is { } guard) return guard;

                result.IsDelete = true;
                foreach (var item in result.invoiceItem)
                {
                    item.IsDelete = true;
                }
                await context.SaveChangesAsync();

                model.Data = true;
                model.Status = 201;
                model.Message = MessageCode.StatusCode.TryGetValue(201, out string messagerss) == true
                    ? model.Message = messagerss
                    : model.Message = "کد وضعیت نامعتبر است.";
                model.Success = true;

                return model;
            }
            model.Data = false;
            model.Status = 404;
            model.Message = "صورتحساب یافت نشد یا قبلاً حذف شده است.";
            model.Success = false;
            return model;

        }

        public async Task<ApiModel> UpdateStatusInvoiceAsync(int InvoiceId)
        {
            ApiModel model = new ApiModel();
            if (InvoiceId == 0)
            {
                model.Data = null;
                model.Status = 400;
                model.Message = MessageCode.StatusCode.TryGetValue(400, out string messagers) == true
                    ? model.Message = messagers
                    : model.Message = "کد وضعیت نامعتبر است.";
                model.Success = false;
                return model;
            }

            var data = await context.Invoices.FindAsync(InvoiceId);
            if (data != null)
            {
                if (await GuardCpInfoAsync(data.CpInfo ?? 0) is { } guard) return guard;

                data.StatusSentToSystem = "4";


                try
                {
                    await context.SaveChangesAsync();
                    model.Data = true;
                }
                catch (Exception)
                {

                    model.Data = false;
                    model.Status = 500;
                    model.Message = MessageCode.StatusCode.TryGetValue(500, out string messagerr) == true
                        ? model.Message = messagerr
                        : model.Message = "کد وضعیت نامعتبر است.";
                    model.Success = false;
                }


                model.Status = 201;
                model.Message = MessageCode.StatusCode.TryGetValue(201, out string messager) == true
                    ? model.Message = messager
                    : model.Message = "کد وضعیت نامعتبر است.";
                model.Success = true;

           
            }
            return model;
        }

        #region مکمل رابط کاربری (شرکت / سال مالی / مشتری)

        public async Task<ApiModel> GetAllCpInfosAsync()
        {
            ApiModel model = new();
            try
            {
                // فقط خدمات‌دهنده‌های شرکت فعال کاربر (حتی برای Admin) — تفکیک کامل بین شرکت‌ها
                var allowed = await AllowedCpInfoIdsAsync();
                var list = await context.CPInfos.Where(c => !c.IsDelete && allowed.Contains(c.Id)).ToListAsync();
                Console.WriteLine($"[Moadian] GetCpInfos user={CurrentUserId} header={httpContextAccessor.HttpContext?.Request?.Headers["X-Company-Id"].FirstOrDefault() ?? "-"} activeCompany={await ResolveActiveCompanyIdAsync()} providers={list.Count} ids=[{string.Join(",", list.Select(c => c.Id))}]");
                var companyNames = await context.SystemCompanies.AsNoTracking()
                    .ToDictionaryAsync(c => c.Id, c => c.Name);
                model.Data = list.Select(c => new VMShowCpinfoByCompany
                {
                    cpinfoid = c.Id,
                    CompanyId = c.CompanyId,
                    CompanyName = c.CompanyId is > 0 && companyNames.TryGetValue(c.CompanyId.Value, out var n) ? n : null,
                    PersianName = c.PersianName,
                    Nationalid = c.Nationalid,
                    EconomicNumber = c.EconomicNumber,
                    Type = string.IsNullOrEmpty(c.Type) ? "حقیقی" : c.Type,
                    EnglishName = c.EnglishName,
                    PostalCode = c.PostalCode
                }).ToList();
                model.Status = 200;
                model.Success = true;
                model.Message = MessageCode.StatusCode.TryGetValue(200, out string msg) ? msg : "کد وضعیت نامعتبر است.";
            }
            catch (Exception ex)
            {
                return new ApiModel { Data = null, Status = 500, Success = false, Message = "خطا در سرور: " + ex.Message };
            }
            return model;
        }

        public async Task<ApiModel> SaveCpInfoAsync(VMShowCpinfoByCompany modeldata)
        {
            ApiModel model = new();
            try
            {
                if (string.IsNullOrWhiteSpace(modeldata.PersianName) || string.IsNullOrWhiteSpace(modeldata.Nationalid))
                    return CreateErrorResponse(400);

                // جلوگیری از ثبت شناسهٔ ملی تکراری (فقط میان رکوردهای فعال — رکوردهای حذف‌نرم‌شده حساب نمی‌شوند)
                if (await context.CPInfos.AnyAsync(c => c.Nationalid == modeldata.Nationalid && !c.IsDelete && c.Id != modeldata.cpinfoid))
                    return new ApiModel { Data = null, Status = 400, Success = false, Message = "شرکتی با این شناسهٔ ملی قبلاً ثبت شده است." };

                // نوع شرکت — فقط دو مقدار معتبر؛ خالی = «حقیقی» (فرم هم همین را پیشنهاد می‌دهد)
                var type = modeldata.Type == "حقوقی" ? "حقوقی" : "حقیقی";
                modeldata.Type = type;

                // شرکت داخلی سیستم: اگر انتخاب نشده، شرکت فعال کاربر؛ باید جزو شرکت‌های مجاز او باشد.
                var accessibleCompanies = await AccessibleCompanyIdsAsync();
                var targetCompanyId = modeldata.CompanyId is > 0
                    ? modeldata.CompanyId.Value
                    : await ResolveActiveCompanyIdAsync();
                Console.WriteLine($"[Moadian] SaveCpInfo user={CurrentUserId} cpinfoid={modeldata.cpinfoid} requestedCompany={modeldata.CompanyId} targetCompany={targetCompanyId} accessible=[{string.Join(",", accessibleCompanies)}]");
                if (targetCompanyId is null || !accessibleCompanies.Contains(targetCompanyId.Value))
                    return new ApiModel { Data = null, Status = 400, Success = false, Message = "شرکت سیستم را از میان شرکت‌هایی که به آن‌ها دسترسی دارید انتخاب کنید." };
                modeldata.CompanyId = targetCompanyId;

                // cpinfoid=0 ← ثبت جدید | cpinfoid>0 ← ویرایش رکورد موجود
                if (modeldata.cpinfoid > 0)
                {
                    var exist = await context.CPInfos.FindAsync(modeldata.cpinfoid);
                    if (exist is null)
                        return new ApiModel { Data = null, Status = 404, Success = false, Message = "شرکت مورد نظر یافت نشد." };
                    if (exist.CompanyId is > 0 && !accessibleCompanies.Contains(exist.CompanyId.Value))
                        return new ApiModel { Data = null, Status = 403, Success = false, Message = "این خدمات‌دهنده مربوط به شرکتی است که شما به آن دسترسی ندارید." };

                    exist.PersianName = modeldata.PersianName;
                    exist.Nationalid = modeldata.Nationalid;
                    exist.EconomicNumber = modeldata.EconomicNumber;
                    exist.Type = type;
                    exist.EnglishName = modeldata.EnglishName;
                    exist.PostalCode = modeldata.PostalCode;
                    exist.CompanyId = targetCompanyId;
                    exist.IsDelete = false;
                    await context.SaveChangesAsync();
                }
                else
                {
                    // شرکت حذف‌نرم‌شده با همین شناسهٔ ملی → بازیابی همان رکورد (id) تا
                    // سوابق آن (سال مالی/اتصال/صورتحساب‌ها) دوباره فعال شود — همان «غیرحذف» کامل
                    var softDeleted = await context.CPInfos
                        .FirstOrDefaultAsync(c => c.Nationalid == modeldata.Nationalid && c.IsDelete);
                    if (softDeleted is not null)
                    {
                        softDeleted.IsDelete = false;
                        softDeleted.PersianName = modeldata.PersianName;
                        softDeleted.EconomicNumber = modeldata.EconomicNumber;
                        softDeleted.Type = type;
                        softDeleted.EnglishName = modeldata.EnglishName;
                        softDeleted.PostalCode = modeldata.PostalCode;
                        softDeleted.CompanyId = targetCompanyId;
                        foreach (var cc in context.ConnectInfos.Where(x => x.CpInfo == softDeleted.Id && x.IsDelete))
                            cc.IsDelete = false;
                        await context.SaveChangesAsync();
                        modeldata.cpinfoid = softDeleted.Id;
                    }
                    else
                    {
                        var cp = new CpInfo
                        {
                            PersianName = modeldata.PersianName,
                            Nationalid = modeldata.Nationalid,
                            EconomicNumber = modeldata.EconomicNumber,
                            Type = type,
                            EnglishName = modeldata.EnglishName,
                            PostalCode = modeldata.PostalCode,
                            CompanyId = targetCompanyId,
                            IsDelete = false
                        };
                        context.CPInfos.Add(cp);
                        await context.SaveChangesAsync();
                        modeldata.cpinfoid = cp.Id;
                    }
                }
                model.Data = modeldata;
                model.Status = 201;
                model.Success = true;
                model.Message = MessageCode.StatusCode.TryGetValue(201, out string msg) ? msg : "کد وضعیت نامعتبر است.";
            }
            catch (Exception ex)
            {
                return new ApiModel { Data = null, Status = 500, Success = false, Message = "خطا در سرور: " + ex.Message };
            }
            return model;
        }

        public async Task<ApiModel> GetAllFiscalYearsAsync(int cpinfoId)
        {
            if (await GuardCpInfoAsync(cpinfoId) is { } guard) return guard;
            ApiModel model = new();
            try
            {
                var list = await context.FiscalYears
                    .Where(f => f.CpInfo == cpinfoId && !f.IsDelete)
                    .OrderByDescending(f => f.StartDate)
                    .ToListAsync();
                model.Data = list.Select(f => new VMFiscalYear
                {
                    Id = f.Id,
                    CpInfoId = cpinfoId,
                    Title = f.Title,
                    StartDate = f.StartDate,
                    EndDate = f.EndDate,
                    IsActive = f.IsActive
                }).ToList();
                model.Status = 200;
                model.Success = true;
                model.Message = MessageCode.StatusCode.TryGetValue(200, out string msg) ? msg : "کد وضعیت نامعتبر است.";
            }
            catch (Exception ex)
            {
                return new ApiModel { Data = null, Status = 500, Success = false, Message = "خطا در سرور: " + ex.Message };
            }
            return model;
        }

        public async Task<ApiModel> SaveFiscalYearAsync(VMFiscalYear modeldata)
        {
            if (await GuardCpInfoAsync(modeldata.CpInfoId) is { } guard) return guard;
            ApiModel model = new();
            try
            {
                if (modeldata.CpInfoId <= 0 || modeldata.EndDate < modeldata.StartDate)
                    return CreateErrorResponse(400);

                var fy = new FiscalYear
                {
                    CpInfo = modeldata.CpInfoId,
                    Title = modeldata.Title,
                    StartDate = modeldata.StartDate,
                    EndDate = modeldata.EndDate,
                    CreatedDate = DateTime.Now,
                    IsActive = modeldata.IsActive,
                    IsDelete = false
                };
                context.FiscalYears.Add(fy);
                await context.SaveChangesAsync();
                modeldata.Id = fy.Id;
                model.Data = modeldata;
                model.Status = 201;
                model.Success = true;
                model.Message = MessageCode.StatusCode.TryGetValue(201, out string msg) ? msg : "کد وضعیت نامعتبر است.";
            }
            catch (Exception ex)
            {
                return new ApiModel { Data = null, Status = 500, Success = false, Message = "خطا در سرور: " + ex.Message };
            }
            return model;
        }

        public async Task<ApiModel> InsertCustomerAsync(VMCustomerList modeldata)
        {
            if (await GuardCpInfoAsync(modeldata.CpInfoId) is { } guard) return guard;
            ApiModel model = new();
            try
            {
                if (string.IsNullOrWhiteSpace(modeldata.Name) || modeldata.CpInfoId <= 0)
                    return CreateErrorResponse(400);

                var customer = new Customer
                {
                    CpInfoId = modeldata.CpInfoId,
                    Name = modeldata.Name,
                    PostalCode = modeldata.PostalCode,
                    Phone = modeldata.Phone,
                    NationalID = modeldata.NationalID,
                    Type = modeldata.Type,
                    EconomicNumber = modeldata.EconomicNumber,
                    Address = modeldata.Address
                };
                context.Customers.Add(customer);
                await context.SaveChangesAsync();
                modeldata.CustomerId = customer.Id;
                model.Data = modeldata;
                model.Status = 201;
                model.Success = true;
                model.Message = MessageCode.StatusCode.TryGetValue(201, out string msg) ? msg : "کد وضعیت نامعتبر است.";
            }
            catch (Exception ex)
            {
                return new ApiModel { Data = null, Status = 500, Success = false, Message = "خطا در سرور: " + ex.Message };
            }
            return model;
        }

        public async Task<ApiModel> UpdateCustomerAsync(VMCustomerList modeldata)
        {
            if (await GuardCpInfoAsync(modeldata.CpInfoId) is { } guard) return guard;
            ApiModel model = new();
            try
            {
                var customer = await context.Customers.FirstOrDefaultAsync(c => c.Id == modeldata.CustomerId);
                if (customer == null)
                    return CreateErrorResponse(404);

                customer.Name = modeldata.Name;
                customer.PostalCode = modeldata.PostalCode;
                customer.Phone = modeldata.Phone;
                customer.NationalID = modeldata.NationalID;
                customer.Type = modeldata.Type;
                customer.EconomicNumber = modeldata.EconomicNumber;
                customer.Address = modeldata.Address;
                await context.SaveChangesAsync();
                model.Data = modeldata;
                model.Status = 200;
                model.Success = true;
                model.Message = MessageCode.StatusCode.TryGetValue(200, out string msg) ? msg : "کد وضعیت نامعتبر است.";
            }
            catch (Exception ex)
            {
                return new ApiModel { Data = null, Status = 500, Success = false, Message = "خطا در سرور: " + ex.Message };
            }
            return model;
        }

        #endregion

    }
        }
