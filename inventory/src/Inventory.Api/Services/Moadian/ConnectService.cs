
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
    public class ConnectService(AppDbContext context) : IConnectService
    {
        public async Task<ApiModel> GetAllGoodOrServiceAsync(int cpinfoId, GetResultQuery request)
        {
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
                catch (Exception e)
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
            if (modeldata.PaymentType == "" || modeldata.CustomerId == 0 || modeldata.StatusSentToSystem == "" ||
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
                            CustomerId = (int)modeldata.CustomerId,
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
                            invoice.CustomerId = (int)modeldata.CustomerId;

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
                                CustomerId = (int)modeldata.CustomerId,
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
                                Inty = 1,
                                Inp = invoice.TypePattern,///نوع صورتحساب invoicePattern
                                Ins = 1,
                                Tins = cpInfo.EconomicNumber,
                                Tinb = customer.EconomicNumber,
                                Bid = customer.NationalID,
                                Tob = customer.Type == "حقیقی" ? 1 : 2,
                                Bpc = customer.PostalCode,
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
                            var packetResponse = response.Body.Result.First();
                            var uid = packetResponse.Uid;
                            var referenceId = packetResponse.ReferenceNumber;
                            //string referenceId = string.Empty;
                            //foreach (var r in responseModels)
                            //    referenceId = r.ReferenceNumber;

                            invoice.ShowButtons = false;

                            var newInvoiceForEdit = context.Invoices.Where(c => c.Id == invoice.Id).FirstOrDefault();
                            newInvoiceForEdit.TaxNumber = taxId;
                            newInvoiceForEdit.StatusSentToSystem = "درانتظار واکنش";
                            newInvoiceForEdit.ReferenceId = referenceId;

                            context.SaveChanges();
                        }
                        else if (modeldata.Subject == "برگشت از فروش")
                        {
                            Invoice newInvoice = new Invoice()
                            {
                                CustomerId = (int)modeldata.CustomerId,
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
                                Inty = 1,
                                Inp = newInvoice.TypePattern,///نوع صورتحساب invoicePattern
                                Ins = 4,
                                Tins = cpInfo.EconomicNumber,
                                Tinb = customer.EconomicNumber,
                                Bid = customer.NationalID,
                                Tob = customer.Type == "حقیقی" ? 1 : 2,
                                Bpc = customer.PostalCode,
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
                            var packetResponse = response.Body.Result.First();
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
                catch (Exception e)
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
            ///C:\Users\ForoughAria\Desktop\key\
            ApiModel model = new ApiModel();
            if (string.IsNullOrEmpty(privatekey) || string.IsNullOrEmpty(shenaseyekta))
            {
                return new ApiModel();
            }
            else
            {
                try
                {

                    //             TaxApiService.Instance.Init(
                    //"A2ZRHM",
                    //new Pkcs8SignatoryConfig(
                    //    @"C:\Users\ForoughAria\Desktop\CSR-M\KELID\Private.key",
                    //    null),
                    //new NormalProperties(ClientType.SELF_TSP),//Tsp(شرکت معتمد)
                    //                                          //Self Tsp(خود مودی
                    //"https://tp.tax.gov.ir/req/api/");




                    TaxApiService.Instance.Init(
shenaseyekta,
new Pkcs8SignatoryConfig(
   privatekey,
    null),
new NormalProperties(ClientType.SELF_TSP),//Tsp(شرکت معتمد)
                                          //Self Tsp(خود مودی
WebServiceAddress);

                    // تست ارتباط با سرور
                    var serverInfo = TaxApiService.Instance.TaxApis.GetServerInformation();

                    if (serverInfo == null)
                    {

                        model.Data = false;
                        model.Status = 500;
                        model.Message = "ارتباط با سرور برقرار نشد";
                        model.Success = false;
                        return model;
                    }
                    var ro = serverInfo.PublicKeys.FirstOrDefault();
                    // تست دریافت توکن
                    var token = TaxApiService.Instance.TaxApis.RequestToken();
                    if (string.IsNullOrEmpty(token.Token))
                    {

                        model.Data = false;
                        model.Status = 500;
                        model.Message = "دریافت توکن ناموفق بود";
                        model.Success = false;
                        return model;
                    }


                    model.Data = true;
                    model.Status = 201;
                    model.Message = " ارتباط با سامانه مودیان با موفقیت برقرار شد";
                    model.Success = true;
                    return model;
                }
                catch (Exception ex)
                {
                    model.Data = false;
                    model.Message = MessageCode.StatusCode.TryGetValue(500, out string messager) == true
                        ? model.Message = messager
                        : model.Message = "کد وضعیت نامعتبر است.";
                    model.Status = 500;
                    model.Success = false;
                    return model;
                }
            }

        }

        /// <summary>تست اتصالِ ثبت‌شدهٔ یک شرکت — کلید را از wwwroot/uploads/{نام شرکت} می‌خواند و احراز هویت واقعی انجام می‌دهد.</summary>
        public async Task<ApiModel> IsConnectStoredAsync(int cpinfoId)
        {
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

        /// <summary>حذف نرم شرکت (و اتصال‌هایش) — صورتحساب‌ها و سوابق حفظ می‌شوند.</summary>
        public async Task<ApiModel> DeleteCpInfoAsync(int id)
        {
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
            ApiModel model = new ApiModel();

            try
            {

                var query = context.Invoices.Where(c => c.CpInfo == CpinfoId).AsQueryable();



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
     (c.StatusSentToSystem == "7" && "تائید شده".Contains(request.SearchTerm))
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
                        i.StatusSentToSystem == "5" ? "دارای خطا" : "بدون نیاز به واکنش",
                    TaxNumber = i.TaxNumber,
                    NewTaxNumber = i.NewTaxNumber,
                    ReferenceId = i.ReferenceId,
                    CustomerName = context.Customers.FirstOrDefault(c => c.Id == i.CustomerId).Name,
                    CustomerId = context.Customers.FirstOrDefault(c => c.Id == i.CustomerId).Id,

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

            string uploadDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/File/Moadian");
            string filePath = Path.Combine(uploadDir, conection.PrivateKeyPath);
            // اگر از SDK رسمی استفاده می‌کنید، امضای بسته به این شکل است
            //var signedPackage = TaxApiService.Instance.SignPackage(invoices, privateKey);
            var isconnect = await IsConnectAsync(conection.TaxMemoryID, filePath, conection.WebServiceAddress);
            if (isconnect.Success)
            {
                return true;
            }

            return false;


        }
        public async Task<ApiModel> SendInvoiceAsync(VmInvoiceItems.InvoiceVM modelInvoiceVM)
        {
            ApiModel model = new ApiModel();
            try
            {

                var conection = ConnectInfo((int)modelInvoiceVM.CPInfoId);
                if (await IsConnectApi((int)modelInvoiceVM.CPInfoId) == true)
                {

                    var invoiceForEdit = context.Invoices.Where(c => c.Id == modelInvoiceVM.Id).FirstOrDefault();
                    DateTimeOffset now = new DateTimeOffset(DateTime.Now);
                    string taxId = TaxApiService.Instance.TaxIdGenerator.GenerateTaxId(
                    conection.TaxMemoryID,
                    modelInvoiceVM.Id, now.DateTime);
                    string inno = modelInvoiceVM.Id.ToString("X").PadLeft(10, '0');
                    long indatim = now.ToUnixTimeMilliseconds();//تاریخ وو زمان صدور صورتحساب
                    //Math.Truncate(price);
                    if (modelInvoiceVM.Subject == "ارسال به سامانه")
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

                            int pamentType = invoiceForEdit.PaymentType == "نقدی" ? 1 : invoiceForEdit.PaymentType == "نسیه" ? 2 : 3;


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
                                //Indati2m = indatim,
                                Inty = 1,//invoiceType
                                Inp = 1, //invoiceForEdit.TypePattern,///نوع صورتحساب invoicePattern
                                Ins = 1,///invoiceSubject
                                Tins = cpInfo.EconomicNumber,
                                Tinb = customer.EconomicNumber,
                                Bid = customer.NationalID,
                                Tob = customer.Type == "حقیقی" ? 1 : 2,
                                Bpc = customer.PostalCode,
                                Tprdis = bodyItems.Sum(x => x.Prdis),
                                Tdis = bodyItems.Sum(x => x.Dis),
                                Tadis = bodyItems.Sum(x => x.Adis),
                                Tvam = bodyItems.Sum(x => x.Vam),
                                Todam = 0,
                                Tbill = bodyItems.Sum(x => x.Tsstam),///کل صورتحساب
                                Setm = pamentType, ///نوع پرداخت
                                //Cap = pamentType == 3 ? bodyItems.Sum(c => c.Cop) : 0,//مقدار نقدی   cashPayment
                                //Insp = pamentType == 3 ? (bodyItems.Sum(x => x.Tsstam) - bodyItems.Sum(c => c.Cop)) : 0////تست شود مقدار نسیه  installmentPayment


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
                            var packetResponse = response.Body.Result.First();
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
                            model.Message = MessageCode.StatusCode.TryGetValue(500, out string messagerss) == true
                                ? model.Message = messagerss
                                : model.Message = ex.Message;
                            model.Status = 500;
                            model.Success = false;
                            return model;
                        }

                    }
                    else if (modelInvoiceVM.Subject == "برگشت از فروش")
                    {
                        var invoice = new Invoice()
                        {
                            CustomerId = (int)invoiceForEdit.CustomerId,
                            DateOrder = DateOnly.FromDateTime(DateTime.Now),
                            PaymentType = invoiceForEdit.PaymentType,
                            Description = invoiceForEdit.Description,
                            CpInfo = invoiceForEdit.CpInfo,
                            /// StatusSentToSystem = modeldata.StatusSentToSystem,
                            StatusSentToSystem = "6",//عدم نیاز به واکنش
                            ShowButtons = false,
                            Subject = "ابطالی"
                        };

                        await context.AddAsync(invoice);
                        await context.SaveChangesAsync();
                        //تولید شماره مالیاتی یکتا برای صورتحساب

                        DateTimeOffset now1 = new DateTimeOffset(DateTime.Now);
                        string taxId1 = TaxApiService.Instance.TaxIdGenerator.GenerateTaxId(
                        conection.TaxMemoryID,
                        invoice.Id, now1.DateTime);
                        string inno1 = invoiceForEdit.Id.ToString("X").PadLeft(10, '0');
                        long indatim1 = now.ToUnixTimeMilliseconds();



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

                        int pamentType = invoiceForEdit.PaymentType == "نقدی" ? 1 : invoiceForEdit.PaymentType == "نسیه" ? 2 : 3;

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
                            Inty = 1,
                            Inp = invoiceForEdit.TypePattern,///نوع صورتحساب invoicePattern
                            Ins = 4,
                            Tins = cpInfo.EconomicNumber,
                            Tinb = customer.EconomicNumber,
                            Bid = customer.NationalID,
                            Tob = customer.Type == "حقیقی" ? 1 : 2,
                            Bpc = customer.PostalCode,
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
                        var packetResponse = response.Body.Result.First();
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

                }
                model.Data = null;
                model.Status = 400;
                model.Message = MessageCode.StatusCode.TryGetValue(400, out string messager) == true
                    ? model.Message = messager
                    : model.Message = "کد وضعیت نامعتبر است.";
                model.Success = false;
                return model;
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
            if (modeldata.IsNew)
                return await CreateInvoiceAsync(modeldata);

            return await UpdateInvoiceAsync(modeldata);
        }

        public async Task<ApiModel> GetGoodOrServiceForComboAsync(int cpinfoid)
        {
            ApiModel model = new ApiModel();
            var result = await context.GoodsOrServices.Where(c => c.CpInfo == cpinfoid && !c.IsDelete).OrderBy(c => c.Name).Select(c => new VMGoodsOrServicesList
            {
                Name = c.Name,
                GoodsOrServicesId = c.Id,
                Price = c.Price,
                UniqueIdentifier = c.UniqueIdentifier,
                ValueAddedPercentage = c.ValueAddedPercentage

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
                model.Data = null;
                model.Message = MessageCode.StatusCode.TryGetValue(404, out string messager) == true
                    ? model.Message = messager
                    : model.Message = "کد وضعیت نامعتبر است.";
                model.Status = 404;
                model.Success = false;

                return model;
            }
        }

        public async Task<ApiModel> GetCustomerForComboAsync(int cpinfoid)
        {
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
                model.Data = null;
                model.Message = MessageCode.StatusCode.TryGetValue(404, out string messager) == true
                    ? model.Message = messager
                    : model.Message = "کد وضعیت نامعتبر است.";
                model.Status = 404;
                model.Success = false;

                return model;
            }
        }

        public async Task<ApiModel> GetUnitOfMeasurementForComboAsync()
        {
            ApiModel model = new ApiModel();
            var result = await context.UnitOfMeasurements.OrderBy(c => c.Name).Select(c => new VMUnitOfMeasurement
            {
                Name = c.Name,
                UnitOfMeasurementId = c.Id
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
                model.Data = null;
                model.Message = MessageCode.StatusCode.TryGetValue(404, out string messager) == true
                    ? model.Message = messager
                    : model.Message = "کد وضعیت نامعتبر است.";
                model.Status = 404;
                model.Success = false;

                return model;
            }
        }

        public async Task<ApiModel> UpdateGoodOrServiceAsync(VMGoodsOrService modeldata)
        {
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
                catch (Exception e)
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
                    var invoice = await context.Invoices.Include(c => c.fiscalYear).FirstOrDefaultAsync(c => c.Id == InvoiceId);
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

                    if (invoice == null)
                        return new ApiModel();
                    invoiceInfo.CustomerId = invoice.CustomerId;
                    invoiceInfo.TaxNumber = invoice.TaxNumber;
                    invoiceInfo.PaymentType = invoice.PaymentType;
                    invoiceInfo.CPInfoId = context.CPInfos.First(c => c.Id == invoice.CpInfo).Id;
                    invoiceInfo.IsNew = false;
                    invoiceInfo.NumberInvoice = invoice.InvoiceNumber;
                    invoiceInfo.FiscalYearId = date;
                    invoiceInfo.DateEndFiscalYear = invoice.fiscalYear.EndDate;
                    invoiceInfo.DatestartFiscalYear = invoice.fiscalYear.StartDate;

                    invoiceInfo.DateOrder = invoice.DateOrder;
                    invoiceInfo.TypePattern = invoice.TypePattern == 1 ? "نوع اول" : invoice.TypePattern == 2 ? "نوع دوم" : "نوع سوم";
                    invoiceInfo.Description = invoice.Description;
                    invoiceInfo.Name = invoice.Subject;
                    invoiceInfo.TaxMemoryID = context.ConnectInfos.FirstOrDefault(c => c.CpInfo == invoice.CpInfo).TaxMemoryID;
                    invoiceInfo.NewTaxNumber = invoice.NewTaxNumber;

                    // ابتدا مقدار NewTaxNumber رو بررسی کن، اگر خالی بود از TaxNumber استفاده کن

                    //// مقدار NewTaxNumber رو هم در invoiceInfo ذخیره کن


                    invoiceInfo.Subject = invoice.Subject;
                    invoiceInfo.StatusSentToSystem = invoice.StatusSentToSystem == "0" ? "ارسال نشده" :
            invoice.StatusSentToSystem == "1" ? "ارسال شده در انتظار استعلام" :
            invoice.StatusSentToSystem == "2" ? "در انتظار بررسی کارپوشه" :
            invoice.StatusSentToSystem == "3" ? "ارسال شده" :
            invoice.StatusSentToSystem == "4" ? "ارسال شده در انتظار واکنش" :
           invoice.StatusSentToSystem == "5" ? "دارای خطا" : "بدون نیاز به واکنش";
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

                        case "FAILED":
                            currentInvoice.StatusSentToSystem = "5";
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
            ApiModel model = new ApiModel();
            if (modeldata.PaymentType == "" || modeldata.CustomerId == 0 || modeldata.StatusSentToSystem == "" ||
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
                            CustomerId = (int)modeldata.CustomerId,
                            // DateOrder = DateTime.Now.ToString(),
                            DateOrder = (DateOnly)modeldata.DateOrder,
                            TypePattern = modeldata.TypePattern == "نوع اول" ? 1 : modeldata.TypePattern == "نوع دوم" ? 2 : 3,
                            PaymentType = modeldata.PaymentType,
                            PriceNaghd=modeldata.PriceNaghd,
                            PriceNesye=modeldata.PriceNesye,
                            Description = modeldata.Description,
                            StatusSentToSystem = "1",///"ارسال شده در انتظار واکنش"
                            ShowButtons = true,
                            CpInfo = modeldata.CPInfoId,
                            Subject = "اصلاحی"
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
                            Inty = 1,
                            Inp = invoice.TypePattern,///نوع صورتحساب invoicePattern
                            Ins = 2,
                            Tins = cpInfo.EconomicNumber,
                            Tinb = customer.EconomicNumber,
                            Bid = customer.NationalID,
                            Tob = customer.Type == "حقیقی" ? 1 : 2,
                            Bpc = customer.PostalCode,
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
                        var packetResponse = response.Body.Result.First();
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
                catch (Exception e)
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
            ApiModel model = new ApiModel();
            if (modeldata.PaymentType == "" || modeldata.CustomerId == 0 || modeldata.StatusSentToSystem == "" ||
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
                            CustomerId = (int)modeldata.CustomerId,
                            DateOrder = DateOnly.FromDateTime(DateTime.Now),
                            PaymentType = modeldata.PaymentType,
                            Description = modeldata.Description,
                            CpInfo = modeldata.CPInfoId,
                            TypePattern = modeldata.TypePattern == "نوع اول" ? 1 : modeldata.PaymentType == "نوع دوم" ? 2 : 3,
                            StatusSentToSystem = "6",//عدم نیاز به واکنش
                            ShowButtons = false,
                            Subject = "ابطالی"
                        };

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
                            Inty = 1,
                            Inp = invoice.TypePattern,///نوع صورتحساب invoicePattern
                            Ins = 3,
                            Tins = cpInfo.EconomicNumber,
                            Tinb = customer.EconomicNumber,
                            Bid = customer.NationalID,
                            Tob = customer.Type == "حقیقی" ? 1 : 2,
                            Bpc = customer.PostalCode,
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
                        var packetResponse = response.Body.Result.First();
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
                catch (Exception e)
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
                catch (Exception e)
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
                catch (Exception e)
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

        public async Task<ApiModel> GetAllUnitOfMeasurementAsync(GetResultQuery request)
        {
            ApiModel model = new ApiModel();

            try
            {

                var query = context.UnitOfMeasurements.AsQueryable();


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
                var respose = new VMUnitOfMeasurement
                {
                    Name = result.Name,
                    Code = result.Code,

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
                catch (Exception e)
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
            
            if (modeldata.PaymentType == "" || modeldata.CustomerId == 0 || modeldata.StatusSentToSystem == "" ||
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
            invoiceForEdit.CustomerId = (int)modeldata.CustomerId;
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
         
            invoiceForEdit.TypePattern = modeldata.TypePattern == "نوع اول" ? 1 : modeldata.TypePattern == "نوع دوم" ? 2 : 3;

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

            // Validation اولیه
            if (modeldata.PaymentType == "" || modeldata.CustomerId == 0 || modeldata.StatusSentToSystem == "" ||
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
                // چک مشتری
                var customerExists = await context.Customers.AnyAsync(c => c.Id == modeldata.CustomerId);
                if (!customerExists)
                {
                    model.Success = false;
                    model.Status = 400;
                    model.Message = "مشتری موردنظر یافت نشد.";
                    return model;
                }

                try
                {
                    // ایجاد فاکتور
                    var invoice = new Invoice()
                    {
                        CustomerId = (int)modeldata.CustomerId,
                        CpInfo = modeldata.CPInfoId,
                        InvoiceNumber = modeldata.NumberInvoice,
                        FiscalYearId = Convert.ToInt32(modeldata.FiscalYearId),
                        DateOrder = (DateOnly)modeldata.DateOrder,
                        TypePattern = modeldata.TypePattern == "نوع اول" ? 1 : modeldata.TypePattern == "نوع دوم" ? 2 : 3,
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
            catch (Exception e)
            {

                throw;
            }
           

           

         
        }

    
   
        //    private async Task<ApiModel> CreateInvoiceAsync(VmInvoiceItems.InvoiceVM modeldata)
        //    {
        //        ApiModel model = new ApiModel();
        //        if (modeldata.PaymentType == "" || modeldata.CustomerId == 0 || modeldata.StatusSentToSystem == "" ||
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
        //                    CustomerId = (int)modeldata.CustomerId,
        //                    CpInfo = modeldata.CPInfoId,
        //                    InvoiceNumber = modeldata.NumberInvoice,
        //                    FiscalYearId = Convert.ToInt32(modeldata.FiscalYearId),
        //                    DateOrder = (DateOnly)modeldata.DateOrder,
        //                    TypePattern = modeldata.TypePattern == "نوع اول" ? 1 : modeldata.TypePattern == "نوع دوم" ? 2 : 3,
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
            var result = await context.Invoices.Include(i => i.invoiceItem).FirstOrDefaultAsync(c => c.Id == InvoiceId);

            if (result is not null)

            {
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
            model.Status = 500;
            model.Message = MessageCode.StatusCode.TryGetValue(500, out string messager) == true
                ? model.Message = messager
                : model.Message = "کد وضعیت نامعتبر است.";
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
                var list = await context.CPInfos.Where(c => !c.IsDelete).ToListAsync();
                model.Data = list.Select(c => new VMShowCpinfoByCompany
                {
                    cpinfoid = c.Id,
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

                // جلوگیری از ثبت شناسهٔ ملی تکراری (به‌جز خودِ رکورد در حالت ویرایش)
                if (await context.CPInfos.AnyAsync(c => c.Nationalid == modeldata.Nationalid && c.Id != modeldata.cpinfoid))
                    return new ApiModel { Data = null, Status = 400, Success = false, Message = "شرکتی با این شناسهٔ ملی قبلاً ثبت شده است." };

                // نوع شرکت — فقط دو مقدار معتبر؛ خالی = «حقیقی» (فرم هم همین را پیشنهاد می‌دهد)
                var type = modeldata.Type == "حقوقی" ? "حقوقی" : "حقیقی";
                modeldata.Type = type;

                // cpinfoid=0 ← ثبت جدید | cpinfoid>0 ← ویرایش رکورد موجود
                if (modeldata.cpinfoid > 0)
                {
                    var exist = await context.CPInfos.FindAsync(modeldata.cpinfoid);
                    if (exist is null)
                        return new ApiModel { Data = null, Status = 404, Success = false, Message = "شرکت مورد نظر یافت نشد." };

                    exist.PersianName = modeldata.PersianName;
                    exist.Nationalid = modeldata.Nationalid;
                    exist.EconomicNumber = modeldata.EconomicNumber;
                    exist.Type = type;
                    exist.EnglishName = modeldata.EnglishName;
                    exist.PostalCode = modeldata.PostalCode;
                    exist.IsDelete = false;
                    await context.SaveChangesAsync();
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
                        IsDelete = false
                    };
                    context.CPInfos.Add(cp);
                    await context.SaveChangesAsync();
                    modeldata.cpinfoid = cp.Id;
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
