﻿using DotNetNuke.Entities.Users;
using Milton.Modules.eClaim.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.UI.WebControls;
using DotNetNuke.Services.Localization;
using DotNetNuke.Services.Exceptions;
using System.Text;
using DotNetNuke.UI.Skins;
using DotNetNuke.UI.Skins.Controls;
using DotNetNuke.Common;
using System.Web;
using System.IO;
using System.Web.Script.Serialization;
using System.Runtime.Serialization.Json;
using Newtonsoft.Json;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System.Collections;

namespace Milton.Modules.eClaim
{
    public partial class ClaimForm : eClaimModuleBase
    {
        public string appId;
        protected void Page_Load(object sender, EventArgs e)
        {
            //DotNetNuke.UI.Utilities.ClientAPI.RegisterClientReference(this.Page, DotNetNuke.UI.Utilities.ClientAPI.ClientNamespaceReferences.dnn);
            //DotNetNuke.Framework.JavaScriptLibraries.JavaScript.RequestRegistration(DotNetNuke.Framework.JavaScriptLibraries.CommonJs.DnnPlugins);

            if (!Page.IsPostBack) {
                btnRelease.Visible = false;
                btnSubmit.Visible = false;
                btnSave2.Visible = false;
                btnDelete.Visible = false;
                btnReport.Visible = false;
                btnBack.Visible = true;
                //basic info (6 item: id, name, region, dept, FormID, status, regionCurr)
                var usr = DotNetNuke.Entities.Users.UserController.Instance.GetUsersBasicSearch(PortalId, 0, 100, "UserID", false, "UserID", UserId.ToString()).Where(u => u.UserID == Convert.ToInt32(UserId)).First();
                var loginID = usr.UserID.ToString();
                var loginRefNo = "ECF-" + DateTime.Now.ToString("yyMMdd") + "-" + DateTime.Now.ToString("HHmmss") + UserId;
                var loginName = usr.DisplayName;
                var loginRegion = usr.Profile.GetPropertyValue("RegionCode");
                var loginDept = usr.Profile.GetPropertyValue("Department");
                var FormID = Request.QueryString["id"];
                var status = "-1";
                var usingCurrency = new CurrencyController().GetCurrencyByRegion(loginRegion).First().CurrencyCode;

                if (string.IsNullOrWhiteSpace(loginRegion))
                {
                    //user does not have region
                    //Skin.AddModuleMessage(this, "Cannot find your region data, please connect to IT department.", ModuleMessage.ModuleMessageType.RedError);
                    //return;
                    throw new Exception("Cannot find user region data");
                }
                var checkRegion = new ClaimFormTypeController().GetClaimFormTypeByRegion(loginRegion);
                if (checkRegion.Count() == 0)
                {
                    //wrong region code
                    //Skin.AddModuleMessage(this, "Your region code is set incorrectly or the eClaim is not ready for your region, please connect to IT department.", ModuleMessage.ModuleMessageType.RedError);
                    //return;
                    throw new Exception("User region code is set incorrectly or the eClaim is not ready for user region");
                }
            
                hfLoginID.Value = loginID;
                hfLoginRefNo.Value = loginRefNo;
                hfLoginName.Value = loginName;
                hfLoginRegion.Value = loginRegion;
                hfLoginDepartment.Value = loginDept;
                hfFormID.Value = FormID;
                hfStatus.Value = status;
                hfusingCurrency.Value = usingCurrency;
                hfCurrDate.Value = DateTime.Now.Date.ToString("yyyy-MM-dd");

                bool canReleaseForm = false;
                bool canEditForm = false;
                bool canConfirmForm = false;
                bool canPrintPDF = false;
                //check status, switch new form, saved, submited, canceled
                var form = new ClaimFormController().GetClaimFormBySql1(FormID);
                if (FormID == "0")
                {
                    status = "0";
                    hfStatus.Value = status;
                    loadBasicData(loginRegion);
                    //loadFormData(FormID);
                    //loadFormDetailsData(FormID);
                    canEditForm = true;
                    canConfirmForm = true;
                    canPrintPDF = false;
                }
                else if (form.Count() > 0)
                {
                    usingCurrency = new CurrencyController().GetCurrencyByRegion(form.First().Region).First().CurrencyCode;
                    hfusingCurrency.Value = usingCurrency;
                    status = form.First().StatusID.ToString();
                    hfStatus.Value = status;//status = 2
                    loadBasicData(form.First().Region);
                    loadFormData(FormID);
                    loadFormDetailsData(FormID);

                    string documentReady = "";
                    var _actualControl = new AttachmentController();
                    var _actuals = _actualControl.GetActuals(form.First().RefNo);
                    if (_actuals != null)
                        foreach (var actual in _actuals)
                        {
                            documentReady += "$(document).queue(function(){" +
                                "$('#liActualAttached').append(liActualFile(" + actual.ID + ",'" + Path.GetFileName(actual.AttachmentPath) + "','" + actual.AttachmentPath + "'));"
                            + "$(this).dequeue(); });";
                        }
                    Page.ClientScript.RegisterStartupScript(this.GetType(), "PageLoadDocumentReady", "$(document).ready(function(){" +
                                documentReady +
                             "});", true);
                    //SUPER
                    var getPosition = new PositionController().GetPositionByStaffID(Convert.ToInt32(loginID));
                    if (getPosition.Count() > 0)
                    {
                        //if position.region contains form.region
                        var StaffPosition = getPosition.First().StaffPosition;
                        var region = getPosition.First().Region;
                        if (StaffPosition == "SuperUser")
                        {
                            //region
                            if (!region.Contains(form.First().Region))
                            {
                                Response.Redirect(Globals.NavigateURL());
                            }
                            if (status == "1")
                            {
                                canReleaseForm = false;
                                canEditForm = true;
                                canConfirmForm = true;
                                canPrintPDF = true;
                            }
                            if (status == "2")
                            {
                                canReleaseForm = true;
                                canEditForm = false;
                                canConfirmForm = false;
                                canPrintPDF = true;
                            }
                            if (status == "3")
                            {
                                canReleaseForm = false;
                                canEditForm = false;
                                canConfirmForm = false;
                                canPrintPDF = false;
                            }
                        }
                        else if (StaffPosition == "Admin")
                        {
                            //1. get all user that admin can add
                            //2. if the user == result got from query from token input
                            //3. show it
                            //1
                            string teamMembers =  getTeamMembersByAdmin(UserId.ToString());
                            if(!teamMembers.Contains(form.First().CreatedBy.ToString())&& form.First().CreatedBy!=UserId)
                                Response.Redirect(Globals.NavigateURL());
                            
                            if (status == "1")
                            {
                                canReleaseForm = false;
                                canEditForm = true;
                                canConfirmForm = true;
                                canPrintPDF = true;
                            }
                            if (status == "2")
                            {
                                canReleaseForm = false;
                                canEditForm = false;
                                canConfirmForm = false;
                                canPrintPDF = true;
                            }
                            if (status == "3")
                            {
                                canReleaseForm = false;
                                canEditForm = false;
                                canConfirmForm = false;
                                canPrintPDF = false;
                            }
                        }
                        else if (StaffPosition == "Acc")
                        {
                            //region
                            if (!region.Contains(form.First().Region))
                            {
                                Response.Redirect(Globals.NavigateURL());
                            }
                            if (status == "1")
                            {
                                canReleaseForm = false;
                                canEditForm = false;
                                canConfirmForm = false;
                                canPrintPDF = true;
                                if (form.First().CreatedBy == usr.UserID)
                                {
                                    canEditForm = true;
                                    canConfirmForm = true;
                                }
                            }
                            if (status == "2")
                            {
                                canReleaseForm = true;
                                canEditForm = false;
                                canConfirmForm = false;
                                canPrintPDF = true;
                            }
                            if (status == "3")
                            {
                                canReleaseForm = false;
                                canEditForm = false;
                                canConfirmForm = false;
                                canPrintPDF = false;
                            }
                        }
                        else if (StaffPosition == "AdminAcc")
                        {
                            string teamMembers = getTeamMembersByAdmin(UserId.ToString());
                            var AdminRegion = region.Split('/')[0];
                            var AccRegion = region.Split('/')[1];
                            bool isAcc = AccRegion.Contains(form.First().Region);
                            bool isAdmin = teamMembers.Contains(form.First().CreatedBy.ToString());
                            if (!isAcc && !isAdmin && form.First().CreatedBy != UserId)
                            {
                                Response.Redirect(Globals.NavigateURL());
                            }
                            //if position.region contains form.region
                            if (status == "1")
                            {
                                if (isAdmin) { 
                                    canEditForm = true;
                                    canConfirmForm = true;
                                }
                                canPrintPDF = true;
                            }
                            if (status == "2")
                            {
                                if (isAcc)
                                    canReleaseForm = true;
                                canConfirmForm = false;
                                canEditForm = false;
                                canPrintPDF = true;
                            }
                            if (status == "3")
                            {
                                canReleaseForm = false;
                                canConfirmForm = false;
                                canEditForm = false;
                                canPrintPDF = false;
                            }
                        }
                        else
                        {//cannot find position
                            Response.Redirect(Globals.NavigateURL());
                        }
                    }
                    else
                    {
                        if (form.First().CreatedBy == usr.UserID)
                        {//creator
                            if (status == "1")
                            {
                                canEditForm = true;
                                canConfirmForm = true;
                                canPrintPDF = true;
                            }
                            if (status == "2")
                            {
                                canEditForm = false;
                                canConfirmForm = false;
                                canPrintPDF = true;
                            }
                            if (status == "3")
                            {
                                canEditForm = false;
                                canConfirmForm = false;
                                canPrintPDF = false;
                            }
                        }
                        else
                        {//normal user
                            Response.Redirect(Globals.NavigateURL());
                        }
                    }
                }
                else
                {
                    Response.Redirect(Globals.NavigateURL());
                }
                if (canReleaseForm)
                {
                    btnRelease.Visible = true;
                }
                if (canEditForm)
                {
                    btnSave2.Visible = true;
                    btnDelete.Visible = true;
                }else
                {
                    hfCanEditForm.Value = "false";
                }
                if (canConfirmForm)
                {
                    btnSubmit.Visible = true;
                }
                if (canPrintPDF)
                {
                    btnReport.Visible = true;
                }
            }
        }
        protected void loadFormDetailsData(string FormID)
        {
            ClaimFormDetailsController CFDCCtl = new ClaimFormDetailsController();
            var FormDetailsData = CFDCCtl.GetClaimFormDetailsBySql1(FormID);
            JavaScriptSerializer Serializer = new JavaScriptSerializer();
            hfFormDetailsData.Value = Serializer.Serialize(FormDetailsData);
            
        }
        protected void loadFormData(string FormID)
        {
            ClaimFormController CFCtl = new ClaimFormController();
            var FormData = CFCtl.GetClaimFormBySql1(FormID);

            hfFormDataCreateBy.Value = UserController.Instance.GetUserById(PortalId, FormData.First().CreatedBy).DisplayName;
            
            JavaScriptSerializer Serializer = new JavaScriptSerializer();
            hfFormData.Value = Serializer.Serialize(FormData);
        }
        protected void loadBasicData(string userRegion)
        {
            var getBU = new BusinessUnitController().GetBusinessUnit();
            var getFormType = new ClaimFormTypeController().GetClaimFormTypeByRegion(userRegion);
            var getCostTypes = new CostTypeController().GetCostTypeByRegion(userRegion);
            //var getCurrencys = new CurrencyController().GetCurrencys();
            var getDistCurrencys = new vw_DistinctCurrencyController().GetDistinctCurrency();
            var getGSTCodes = new GSTCodeController().GetGSTCodeByRegion(userRegion);
            var getTransportationTypes = new TransportationTypeController().GetTransportationTypeByRegion(userRegion);

            JavaScriptSerializer Serializer = new JavaScriptSerializer();

            hfBU.Value = Serializer.Serialize(getBU);
            hfFormType.Value = Serializer.Serialize(getFormType);
            hfCostType.Value = Serializer.Serialize(getCostTypes);
            //hfCurrency.Value = Serializer.Serialize(getCurrencys);
            hfDistCurr.Value = Serializer.Serialize(getDistCurrencys);
            hfGSTCode.Value = Serializer.Serialize(getGSTCodes);
            hfTransportationType.Value = Serializer.Serialize(getTransportationTypes);
        }
        protected void saveAndSubmit(int nextStatus) {
            JavaScriptSerializer Serializer = new JavaScriptSerializer();
            ClaimFormController CFCtl = new ClaimFormController();
            ClaimFormDetailsController CFDCtl = new ClaimFormDetailsController();
            var usr = DotNetNuke.Entities.Users.UserController.Instance.GetUsersBasicSearch(PortalId, 0, 100, "UserID", false, "UserID", UserId.ToString()).Where(u => u.UserID == Convert.ToInt32(UserId)).First();
            //Form
            var fc = CFCtl.GetClaimFormBySql1(hfFormID.Value);
            var FormObjForID = new Components.ClaimForm();
            var jsFormData = JsonConvert.DeserializeObject<TempForm>(hfFormData.Value);
            if (fc.Count() == 0)
            {
                var newForm = new Components.ClaimForm();
                if (string.IsNullOrWhiteSpace(jsFormData.SubmissionDate))
                {
                    //if(jsFormData.SubmissionDate == null), no need to set
                    //newForm.SubmissionDate = null;
                }
                else
                {
                    if (nextStatus == 1)
                    {

                    }
                    else if (nextStatus == 2)
                    {
                        //newForm.SubmissionDate = Convert.ToDateTime(jsFormData.SubmissionDate);
                        newForm.SubmissionDate = DateTime.Now;
                        newForm.ConfirmedBy = usr.UserID;
                        newForm.ConfirmDate = DateTime.Now;
                    }
                    else if (nextStatus == 3)//delete
                    {
                        //newForm.SubmissionDate = null;
                    }
                    
                }
                newForm.RefNo = jsFormData.RefNo;
                newForm.Region = jsFormData.Region;
                newForm.BusinessUnit = jsFormData.BusinessUnit;
                newForm.ClaimFormType = jsFormData.ClaimFormType;
                newForm.JobNo = jsFormData.JobNo;
                newForm.ProjectName = jsFormData.ProjectName;
                //newForm.ProjectName = hfFormDetailsData.Value;
                newForm.LastUpdatedBy = usr.UserID;
                newForm.LastUpdateDate = DateTime.Now;
                newForm.StatusID = nextStatus;
                newForm.CreatedBy = usr.UserID;
                newForm.CreateDate = DateTime.Now;
                FormObjForID = CFCtl.CreateClaimForm(newForm);
            }
            else if (fc.Count() > 0)
            {
                var newForm = fc.First();
                if (string.IsNullOrWhiteSpace(jsFormData.SubmissionDate))
                {
                    //newForm.SubmissionDate = null;
                }
                else
                {
                    if (nextStatus == 1)
                    {
                        newForm.SubmissionDate = null;
                    }
                    else if (nextStatus == 2)
                    {
                        //newForm.SubmissionDate = Convert.ToDateTime(jsFormData.SubmissionDate);
                        newForm.SubmissionDate = DateTime.Now;
                        newForm.ConfirmedBy = usr.UserID;
                        newForm.ConfirmDate = DateTime.Now;
                    }
                    else if (nextStatus == 3)
                    {
                        //newForm.SubmissionDate = null;
                    }
                }
                newForm.BusinessUnit = jsFormData.BusinessUnit;
                newForm.ClaimFormType = jsFormData.ClaimFormType;
                newForm.JobNo = jsFormData.JobNo;
                newForm.ProjectName = jsFormData.ProjectName;
                //newForm.ProjectName = hfFormDetailsData.Value;
                newForm.LastUpdatedBy = usr.UserID;
                newForm.LastUpdateDate = DateTime.Now;
                newForm.StatusID = nextStatus;
                FormObjForID = CFCtl.UpdateClaimForm(newForm);
            }
            appId = FormObjForID.ID.ToString();
            //Form Details
            TempFormDetails jsFormDetailsData = JsonConvert.DeserializeObject<TempFormDetails>(hfFormDetailsData.Value);
            CFDCtl.DeleteClaimFormDetails(FormObjForID.ID.ToString());
            List<TempFormDetailsData> formDetailsList = new List<TempFormDetailsData>();
            foreach (object _data in jsFormDetailsData.data)
            {
                formDetailsList.Add(JsonConvert.DeserializeObject<TempFormDetailsData>(Convert.ToString(_data)));
            }
            foreach (var _data in formDetailsList)
            {
                var newFormDetails = new ClaimFormDetails();
                newFormDetails.ClaimFormID = FormObjForID.ID;
                newFormDetails.SeqNo = Convert.ToInt32(_data.SeqNo);
                newFormDetails.CostTypeID = Convert.ToInt32(_data.CostTypeID);
                if (string.IsNullOrWhiteSpace(_data.VoucDate))
                {
                    newFormDetails.VoucDate = null;
                }
                else
                {
                    newFormDetails.VoucDate = Convert.ToDateTime(_data.VoucDate);
                }

                if (string.IsNullOrWhiteSpace(_data.PeriodFrom))
                {
                    newFormDetails.PeriodFrom = null;
                }
                else
                {
                    newFormDetails.PeriodFrom = Convert.ToDateTime(_data.PeriodFrom);
                }

                if (string.IsNullOrWhiteSpace(_data.PeriodTo))
                {
                    newFormDetails.PeriodTo = null;
                }
                else
                {
                    newFormDetails.PeriodTo = Convert.ToDateTime(_data.PeriodTo);
                }
                newFormDetails.LocationFrom = _data.LocationFrom;
                newFormDetails.LocationTo = _data.LocationTo;
                newFormDetails.TransportationTypeID = Convert.ToInt32(_data.TransportationTypeID); ;
                newFormDetails.Description = _data.Description;
                //newFormDetails.AutoCal = _data.AutoCal;
                newFormDetails.AutoCal = "A";
                newFormDetails.OriginalCurrencyCode = _data.OriginalCurrencyCode;
                newFormDetails.OriginalAmount = Convert.ToDouble(_data.OriginalAmount);
                newFormDetails.TaxAmount = Convert.ToDouble(_data.TaxAmount);
                newFormDetails.GSTCode = Convert.ToInt32(_data.GSTCode); ;
                newFormDetails.AmountWithTax = Convert.ToDouble(_data.AmountWithTax);
                newFormDetails.ClaimedCurrencyCode = _data.ClaimedCurrencyCode;
                newFormDetails.ExchangeRate = Convert.ToDouble(_data.ExchangeRate);
                newFormDetails.TotalAmount = Convert.ToDouble(_data.TotalAmount);
                newFormDetails.Remarks = _data.Remarks;
                newFormDetails.IncludeRef = _data.IncludeRef;
                CFDCtl.CreateClaimFormDetails(newFormDetails);
            }
        }
        protected void btnRelease_Click(object sender, EventArgs e)
        {
            saveAndSubmit(1);
            Response.Redirect(EditUrl("id", appId, "ClaimForm"));
        }
        protected void btnSave_Click(object sender, EventArgs e)
        {
            saveAndSubmit(1);
            Response.Redirect(EditUrl("id", appId, "ClaimForm"));
        }
        protected void btnSubmit_Click(object sender, EventArgs e)
        {
            saveAndSubmit(2);
            Response.Redirect(Globals.NavigateURL());
        }
        protected void btnDelete_Click(object sender, EventArgs e)
        {
            var usr = DotNetNuke.Entities.Users.UserController.Instance.GetUsersBasicSearch(PortalId, 0, 100, "UserID", false, "UserID", UserId.ToString()).Where(u => u.UserID == Convert.ToInt32(UserId)).First();

            var cfctl = new ClaimFormController();
            var fc = cfctl.GetClaimFormBySql1(hfFormID.Value);
            fc.First().StatusID = 3;
            fc.First().LastUpdatedBy = usr.UserID;
            fc.First().LastUpdateDate = DateTime.Now;
            cfctl.UpdateClaimForm(fc.First());
            Response.Redirect(Globals.NavigateURL());
        }
        protected void btnReport_Click(object sender, EventArgs e)
        {
            try
            {
                int ID = !string.IsNullOrWhiteSpace(hfFormID.Value) ? Convert.ToInt32(hfFormID.Value) : -1;

                if (ID > 0)
                {
                    ClaimFormController _ClaimFormCtl = new ClaimFormController();
                    Components.ClaimForm _ClaimForm = _ClaimFormCtl.GetClaimFormBySql1(hfFormID.Value).Count() == 1 ? _ClaimFormCtl.GetClaimFormBySql1(hfFormID.Value).FirstOrDefault() : null;

                    ClaimFormDetailsController _ClaimFormDetailsCtl = new ClaimFormDetailsController();
                    IEnumerable<ClaimFormDetails> _ClaimFormDetailsList = _ClaimFormDetailsCtl.GetClaimFormDetailsBySql1(hfFormID.Value);

                    if (_ClaimForm != null && _ClaimFormDetailsList != null)
                    {
                        Document doc = new Document(PageSize.A4.Rotate(), 25, 25, 25, 25);

                        MemoryStream memory = new MemoryStream();
                        PdfWriter pdfWriter = PdfWriter.GetInstance(doc, memory);
                        doc.Open();

                        //iTextSharp.text.Image logo = iTextSharp.text.Image.GetInstance(HttpContext.Current.Request.PhysicalApplicationPath + "Portals\\0\\logo.png");
                        //logo.ScaleAbsolute(55, 41);

                        #region Font

                        BaseFont basefont = BaseFont.CreateFont(HttpContext.Current.Request.PhysicalApplicationPath + "Plug-in\\fonts\\simsun.ttc,1", BaseFont.IDENTITY_H, BaseFont.EMBEDDED);

                        Font title = new Font(basefont, 16, Font.BOLD);
                        Font subtitle = new Font(basefont, 12, Font.BOLD);
                        Font subtitle_underline = new Font(basefont, 12, Font.BOLD | Font.UNDERLINE);
                        Font content = new Font(basefont, 10, Font.NORMAL);
                        Font content_small = new Font(basefont, 8, Font.NORMAL);
                        Font content_bold = new Font(basefont, 10, Font.BOLD);
                        Font content_bold_underline = new Font(basefont, 10, Font.BOLD | Font.UNDERLINE);
                        Font content_underline = new Font(basefont, 10, Font.NORMAL | Font.UNDERLINE);
                        Font info = new Font(basefont, 8, Font.ITALIC, BaseColor.LIGHT_GRAY);
                        #endregion

                        #region PDF Header

                        Chunk title_text = new Chunk(Localization.GetString("pdfTitle", LocalResourceFile), title);

                        Chunk staffName_text = new Chunk(
                            string.Format("{0} {1}",
                            Localization.GetString("pdfStaffName", LocalResourceFile),
                            UserController.GetUserById(PortalId, _ClaimForm.CreatedBy) != null ? UserController.GetUserById(PortalId, _ClaimForm.CreatedBy).DisplayName : string.Empty
                            ), content);
                        Chunk staffBU_text = new Chunk(
                            string.Format("{0} {1}",
                            Localization.GetString("pdfStaffBU", LocalResourceFile),
                            _ClaimForm.BusinessUnit
                            ), content);
                        Chunk submissionDate_text = new Chunk(
                            string.Format("{0} {1}",
                            Localization.GetString("pdfSubmissionDate", LocalResourceFile),
                            Convert.ToDateTime(_ClaimForm.SubmissionDate).ToString("yyyy-MM-dd")
                            ), content);
                        Chunk type_text = new Chunk(
                            string.Format("{0} {1}",
                            Localization.GetString("pdfType", LocalResourceFile),
                            displayFormType(_ClaimForm)
                            ), content);

                        PdfPTable formInfo_table = null;
                        formInfo_table = new PdfPTable(new float[] { 5, 5 });
                        formInfo_table.WidthPercentage = 100;

                        PdfPCell formInfo_staffName_cell = new PdfPCell(new Phrase(staffName_text));
                        PdfPCell formInfo_staffBU_cell = new PdfPCell(new Phrase(staffBU_text));
                        PdfPCell formInfo_submissionDate_cell = new PdfPCell(new Phrase(submissionDate_text));
                        PdfPCell formInfo_type_cell = new PdfPCell(new Phrase(type_text));

                        formInfo_staffName_cell.Border = Rectangle.NO_BORDER;
                        formInfo_staffBU_cell.Border = Rectangle.NO_BORDER;
                        formInfo_submissionDate_cell.Border = Rectangle.NO_BORDER;
                        formInfo_submissionDate_cell.HorizontalAlignment = Element.ALIGN_RIGHT;
                        formInfo_type_cell.Border = Rectangle.NO_BORDER;
                        formInfo_type_cell.HorizontalAlignment = Element.ALIGN_RIGHT;

                        formInfo_table.AddCell(formInfo_staffName_cell);
                        formInfo_table.AddCell(formInfo_submissionDate_cell);

                        formInfo_table.AddCell(formInfo_staffBU_cell);
                        formInfo_table.AddCell(formInfo_type_cell);

                        Paragraph header = new Paragraph();

                        header.Alignment = Element.ALIGN_JUSTIFIED;

                        //header.Add(logo);
                        header.Add(title_text);
                        header.Add(Environment.NewLine);
                        header.Add(formInfo_table);
                        header.Add(Environment.NewLine);
                        #endregion

                        #region PDF Details

                        PdfPTable claimDetails_table = null;
                        Hashtable ExclTaxTotal = new Hashtable();
                        Hashtable TaxTotal = new Hashtable();
                        Hashtable InclTaxTotal = new Hashtable();
                        Hashtable ClaimTotal = new Hashtable();

                        claimDetails_table = new PdfPTable(new float[] { 3, 2, 6, 1, 1, 1, 2, 1, 1, 1, 1, 1, 2 });

                        claimDetails_table.WidthPercentage = 100;

                        claimDetails_table.SplitLate = false;

                        Chunk cdHeader_CostType_text = new Chunk(Localization.GetString("pdfCostType", LocalResourceFile), content_bold);
                        Chunk cdHeader_VoucherDate_text = new Chunk(Localization.GetString("pdfVoucherDate", LocalResourceFile), content_bold);
                        Chunk cdHeader_Details_text = new Chunk(Localization.GetString("pdfDetails", LocalResourceFile), content_bold);
                        Chunk cdHeader_OriginalAmount_text = new Chunk(Localization.GetString("pdfOriginalAmount", LocalResourceFile), content_bold);
                        Chunk cdHeader_ExclTax_text = new Chunk(Localization.GetString("pdfExclTax", LocalResourceFile), content_bold);
                        Chunk cdHeader_Tax_text = new Chunk(Localization.GetString("pdfTax", LocalResourceFile), content_bold);
                        Chunk cdHeader_InclTax_text = new Chunk(Localization.GetString("pdfInclTax", LocalResourceFile), content_bold);
                        Chunk cdHeader_ExchangeRate_text = new Chunk(Localization.GetString("pdfExchangeRate", LocalResourceFile), content_bold);
                        Chunk cdHeader_TotalClaimAmount_text = new Chunk(Localization.GetString("pdfTotalClaimAmount", LocalResourceFile), content_bold);
                        //Chunk cdHeader_Ref_text = new Chunk(Localization.GetString("pdfRef", LocalResourceFile), content_bold);
                        Chunk cdHeader_Remark_text = new Chunk(Localization.GetString("pdfRemark", LocalResourceFile), content_bold);

                        PdfPCell cdHeader_CostType_cell = new PdfPCell(new Phrase(cdHeader_CostType_text));
                        PdfPCell cdHeader_VoucherDate_cell = new PdfPCell(new Phrase(cdHeader_VoucherDate_text));
                        PdfPCell cdHeader_Details_cell = new PdfPCell(new Phrase(cdHeader_Details_text));
                        PdfPCell cdHeader_OriginalAmount_cell = new PdfPCell(new Phrase(cdHeader_OriginalAmount_text));
                        PdfPCell cdHeader_ExclTax_cell = new PdfPCell(new Phrase(cdHeader_ExclTax_text));
                        PdfPCell cdHeader_Tax_cell = new PdfPCell(new Phrase(cdHeader_Tax_text));
                        PdfPCell cdHeader_InclTax_cell = new PdfPCell(new Phrase(cdHeader_InclTax_text));
                        PdfPCell cdHeader_ExchangeRate_cell = new PdfPCell(new Phrase(cdHeader_ExchangeRate_text));
                        PdfPCell cdHeader_TotalClaimAmount_cell = new PdfPCell(new Phrase(cdHeader_TotalClaimAmount_text));
                        //PdfPCell cdHeader_Ref_cell = new PdfPCell(new Phrase(cdHeader_Ref_text));
                        PdfPCell cdHeader_Remark_cell = new PdfPCell(new Phrase(cdHeader_Remark_text));

                        cdHeader_CostType_cell.HorizontalAlignment = Element.ALIGN_LEFT;
                        cdHeader_CostType_cell.VerticalAlignment = Element.ALIGN_TOP;
                        cdHeader_CostType_cell.BackgroundColor = BaseColor.LIGHT_GRAY;
                        cdHeader_CostType_cell.Rowspan = 2;

                        cdHeader_VoucherDate_cell.HorizontalAlignment = Element.ALIGN_LEFT;
                        cdHeader_VoucherDate_cell.VerticalAlignment = Element.ALIGN_TOP;
                        cdHeader_VoucherDate_cell.BackgroundColor = BaseColor.LIGHT_GRAY;
                        cdHeader_VoucherDate_cell.Rowspan = 2;

                        cdHeader_Details_cell.HorizontalAlignment = Element.ALIGN_LEFT;
                        cdHeader_Details_cell.VerticalAlignment = Element.ALIGN_TOP;
                        cdHeader_Details_cell.BackgroundColor = BaseColor.LIGHT_GRAY;
                        cdHeader_Details_cell.Rowspan = 2;

                        cdHeader_OriginalAmount_cell.HorizontalAlignment = Element.ALIGN_CENTER;
                        cdHeader_OriginalAmount_cell.VerticalAlignment = Element.ALIGN_TOP;
                        cdHeader_OriginalAmount_cell.BackgroundColor = BaseColor.LIGHT_GRAY;
                        cdHeader_OriginalAmount_cell.Colspan = 6;

                        cdHeader_ExchangeRate_cell.HorizontalAlignment = Element.ALIGN_RIGHT;
                        cdHeader_ExchangeRate_cell.VerticalAlignment = Element.ALIGN_TOP;
                        cdHeader_ExchangeRate_cell.BackgroundColor = BaseColor.LIGHT_GRAY;
                        cdHeader_ExchangeRate_cell.Rowspan = 2;

                        cdHeader_TotalClaimAmount_cell.HorizontalAlignment = Element.ALIGN_RIGHT;
                        cdHeader_TotalClaimAmount_cell.VerticalAlignment = Element.ALIGN_TOP;
                        cdHeader_TotalClaimAmount_cell.BackgroundColor = BaseColor.LIGHT_GRAY;
                        cdHeader_TotalClaimAmount_cell.Rowspan = 2;
                        cdHeader_TotalClaimAmount_cell.Colspan = 2;

                        //cdHeader_Ref_cell.HorizontalAlignment = Element.ALIGN_LEFT;
                        //cdHeader_Ref_cell.VerticalAlignment = Element.ALIGN_TOP;
                        //cdHeader_Ref_cell.BackgroundColor = BaseColor.LIGHT_GRAY;
                        //cdHeader_Ref_cell.Rowspan = 2;

                        cdHeader_Remark_cell.HorizontalAlignment = Element.ALIGN_LEFT;
                        cdHeader_Remark_cell.VerticalAlignment = Element.ALIGN_TOP;
                        cdHeader_Remark_cell.BackgroundColor = BaseColor.LIGHT_GRAY;
                        cdHeader_Remark_cell.Rowspan = 2;

                        cdHeader_ExclTax_cell.HorizontalAlignment = Element.ALIGN_RIGHT;
                        cdHeader_ExclTax_cell.VerticalAlignment = Element.ALIGN_TOP;
                        cdHeader_ExclTax_cell.BackgroundColor = BaseColor.LIGHT_GRAY;
                        cdHeader_ExclTax_cell.Colspan = 2;

                        cdHeader_Tax_cell.HorizontalAlignment = Element.ALIGN_RIGHT;
                        cdHeader_Tax_cell.VerticalAlignment = Element.ALIGN_TOP;
                        cdHeader_Tax_cell.BackgroundColor = BaseColor.LIGHT_GRAY;
                        cdHeader_Tax_cell.Colspan = 2;

                        cdHeader_InclTax_cell.HorizontalAlignment = Element.ALIGN_RIGHT;
                        cdHeader_InclTax_cell.VerticalAlignment = Element.ALIGN_TOP;
                        cdHeader_InclTax_cell.BackgroundColor = BaseColor.LIGHT_GRAY;
                        cdHeader_InclTax_cell.Colspan = 2;

                        claimDetails_table.AddCell(cdHeader_CostType_cell);
                        claimDetails_table.AddCell(cdHeader_VoucherDate_cell);
                        claimDetails_table.AddCell(cdHeader_Details_cell);
                        claimDetails_table.AddCell(cdHeader_OriginalAmount_cell);
                        claimDetails_table.AddCell(cdHeader_ExchangeRate_cell);
                        claimDetails_table.AddCell(cdHeader_TotalClaimAmount_cell);

                        //claimDetails_table.AddCell(cdHeader_Ref_cell);
                        claimDetails_table.AddCell(cdHeader_Remark_cell);
                        claimDetails_table.AddCell(cdHeader_ExclTax_cell);
                        claimDetails_table.AddCell(cdHeader_Tax_cell);
                        claimDetails_table.AddCell(cdHeader_InclTax_cell);

                        int ref_index = 1;

                        Chunk space_text = new Chunk(" ", content);

                        foreach (ClaimFormDetails _cd in _ClaimFormDetailsList)
                        {
                            string _costtype = new CostTypeController().GetCostTypeBySql1(_cd.CostTypeID).Count() == 1 ? new CostTypeController().GetCostTypeBySql1(_cd.CostTypeID).FirstOrDefault().CostTypeDesc : string.Empty;
                            Chunk cdBody_CostType_text = new Chunk(_costtype, content);
                            string _voucherdate = _cd.VoucDate != null ? Convert.ToDateTime(_cd.VoucDate).ToString("yyyy-MM-dd") : string.Empty;
                            Chunk cdBody_VoucherDate_text = new Chunk(_voucherdate, content);

                            string _details_LocFrom = _cd.LocationFrom != null ? _cd.LocationFrom : string.Empty;
                            string _details_LocTo = _cd.LocationTo != null ? _cd.LocationTo : string.Empty;
                            Chunk cdBody_Details_LocFrom_text = new Chunk(_details_LocFrom, content_bold);
                            Chunk cdBody_Details_LocTo_text = new Chunk(_details_LocTo, content_bold);
                            Chunk cdBody_Details_Location1_text = new Chunk(string.Format("{0} {1}", Localization.GetString("pdfLocation", LocalResourceFile), Localization.GetString("pdfFrom", LocalResourceFile)), content);
                            Chunk cdBody_Details_Location2_text = new Chunk(Localization.GetString("pdfTo", LocalResourceFile), content);
                            Phrase cdBody_Details_Location_Phrase = new Phrase();
                            cdBody_Details_Location_Phrase.Add(cdBody_Details_Location1_text);
                            cdBody_Details_Location_Phrase.Add(space_text);
                            cdBody_Details_Location_Phrase.Add(cdBody_Details_LocFrom_text);
                            cdBody_Details_Location_Phrase.Add(space_text);
                            cdBody_Details_Location_Phrase.Add(cdBody_Details_Location2_text);
                            cdBody_Details_Location_Phrase.Add(space_text);
                            cdBody_Details_Location_Phrase.Add(cdBody_Details_LocTo_text);

                            string _details_DateFrom = _cd.PeriodFrom != null ? Convert.ToDateTime(_cd.PeriodFrom).ToString("yyyy-MM-dd") : string.Empty;
                            string _details_DateTo = _cd.PeriodTo != null ? Convert.ToDateTime(_cd.PeriodTo).ToString("yyyy-MM-dd") : string.Empty;
                            Chunk cdBody_Details_DateFrom_text = new Chunk(_details_DateFrom, content_bold);
                            Chunk cdBody_Details_DateTo_text = new Chunk(_details_DateTo, content_bold);
                            Chunk cdBody_Details_Date1_text = new Chunk(string.Format("{0} {1}", Localization.GetString("pdfDate", LocalResourceFile), Localization.GetString("pdfFrom", LocalResourceFile)), content);
                            Chunk cdBody_Details_Date2_text = new Chunk(Localization.GetString("pdfTo", LocalResourceFile), content);
                            Phrase cdBody_Details_Date_Phrase = new Phrase();
                            cdBody_Details_Date_Phrase.Add(cdBody_Details_Date1_text);
                            cdBody_Details_Date_Phrase.Add(space_text);
                            cdBody_Details_Date_Phrase.Add(cdBody_Details_DateFrom_text);
                            cdBody_Details_Date_Phrase.Add(space_text);
                            cdBody_Details_Date_Phrase.Add(cdBody_Details_Date2_text);
                            cdBody_Details_Date_Phrase.Add(space_text);
                            cdBody_Details_Date_Phrase.Add(cdBody_Details_DateTo_text);

                            string _details_TransType = _cd.TransportationTypeID > 0 ? new TransportationTypeController().GetTransportationTypeBySql1(_cd.TransportationTypeID).Count() == 1 ? new TransportationTypeController().GetTransportationTypeBySql1(_cd.TransportationTypeID).FirstOrDefault().TransportationTypeDesc : string.Empty : string.Empty;
                            Chunk cdBody_Details_TransType_text = new Chunk(_details_TransType, content_bold);
                            Chunk cdBody_Details_Transportation_text = new Chunk(Localization.GetString("pdfTransportation", LocalResourceFile), content);
                            Phrase cdBody_Details_Transportation_Phrase = new Phrase();
                            cdBody_Details_Transportation_Phrase.Add(cdBody_Details_Transportation_text);
                            cdBody_Details_Transportation_Phrase.Add(space_text);
                            cdBody_Details_Transportation_Phrase.Add(cdBody_Details_TransType_text);

                            string _details_desc = _cd.Description != null ? _cd.Description : string.Empty;
                            Chunk cdBody_Details_Desc_text = new Chunk(_details_desc, content_bold);
                            Chunk cdBody_Details_Description_text = new Chunk(Localization.GetString("pdfDescription", LocalResourceFile), content);
                            Phrase cdBody_Details_Description_Phrase = new Phrase();
                            cdBody_Details_Description_Phrase.Add(cdBody_Details_Description_text);
                            cdBody_Details_Description_Phrase.Add(space_text);
                            cdBody_Details_Description_Phrase.Add(cdBody_Details_Desc_text);

                            string _details_remarks = _cd.Remarks != null ? _cd.Remarks : string.Empty;
                            Chunk cdBody_Details_Remarks_text = new Chunk(_details_remarks, content_bold);
                            Chunk cdBody_Details_RemarksTitle_text = new Chunk(Localization.GetString("pdfRemarks", LocalResourceFile), content);
                            Phrase cdBody_Details_RemarksTitle_Phrase = new Phrase();
                            cdBody_Details_RemarksTitle_Phrase.Add(cdBody_Details_RemarksTitle_text);
                            cdBody_Details_RemarksTitle_Phrase.Add(space_text);
                            cdBody_Details_RemarksTitle_Phrase.Add(cdBody_Details_Remarks_text);

                            string _originalAmount_currency = _cd.OriginalCurrencyCode != null ? _cd.OriginalCurrencyCode : string.Empty;
                            Chunk cdBody_OriginalAmount_Currency_text = new Chunk(_originalAmount_currency, content);
                            string _originalAmount_exclTax = _cd.OriginalAmount.ToString();
                            Chunk cdBody_OriginalAmount_ExclTax_text = new Chunk(_originalAmount_exclTax, content);
                            string _originalAmount_Tax = _cd.TaxAmount.ToString();
                            string _originalAmount_TaxCode = new GSTCodeController().GetGSTCodeBySql1(_cd.GSTCode).Count() == 1 ? new GSTCodeController().GetGSTCodeBySql1(_cd.GSTCode).FirstOrDefault().TaxDesc : string.Empty;
                            Chunk cdBody_OriginalAmount_Tax_text = new Chunk(string.Format("{0} ({1})", _originalAmount_Tax, _originalAmount_TaxCode), content);
                            string _originalAmount_InclTax = _cd.AmountWithTax.ToString();
                            Chunk cdBody_OriginalAmount_InclTax_text = new Chunk(_originalAmount_InclTax, content);
                            string _exchangerate = _cd.ExchangeRate.ToString();

                            Chunk cdBody_ExchangeRate_text = new Chunk(_exchangerate, content);
                            string _claimcurrency = _cd.ClaimedCurrencyCode != null ? _cd.ClaimedCurrencyCode : string.Empty;
                            Chunk cdBody_ClaimCurrency_text = new Chunk(_claimcurrency, content);
                            string _totalclaimamount = _cd.TotalAmount.ToString();
                            Chunk cdBody_TotalClaimAmount_text = new Chunk(_totalclaimamount, content);

                            //string _ref = _cd.IncludeRef.Equals("Y") ? ref_index.ToString() : string.Empty;
                            //Chunk cdBody_Ref_text = new Chunk(_ref, content);
                            //if (!string.IsNullOrWhiteSpace(_ref))
                            //    ref_index++;

                            string _remark = _cd.Remarks != null ? _cd.Remarks : string.Empty;
                            Chunk cdBody_Remark_text = new Chunk(_remark, content);

                            if (ExclTaxTotal.ContainsKey(_originalAmount_currency))
                                ExclTaxTotal[_originalAmount_currency] = Convert.ToDouble(ExclTaxTotal[_originalAmount_currency]) + _cd.OriginalAmount;
                            else
                                ExclTaxTotal.Add(_originalAmount_currency, _cd.OriginalAmount);

                            if (TaxTotal.ContainsKey(_originalAmount_currency))
                                TaxTotal[_originalAmount_currency] = Convert.ToDouble(TaxTotal[_originalAmount_currency]) + _cd.TaxAmount;
                            else
                                TaxTotal.Add(_originalAmount_currency, _cd.TaxAmount);

                            if (InclTaxTotal.ContainsKey(_originalAmount_currency))
                                InclTaxTotal[_originalAmount_currency] = Convert.ToDouble(InclTaxTotal[_originalAmount_currency]) + _cd.AmountWithTax;
                            else
                                InclTaxTotal.Add(_originalAmount_currency, _cd.AmountWithTax);

                            if (ClaimTotal.ContainsKey(_claimcurrency))
                                ClaimTotal[_claimcurrency] = Convert.ToDouble(ClaimTotal[_claimcurrency]) + _cd.TotalAmount;
                            else
                                ClaimTotal.Add(_claimcurrency, _cd.TotalAmount);

                            PdfPCell cdBody_CostType_cell = new PdfPCell(new Phrase(cdBody_CostType_text));
                            PdfPCell cdBody_VoucherDate_cell = new PdfPCell(new Phrase(cdBody_VoucherDate_text));

                            PdfPCell cdBody_OriginalAmount_Currency_cell = new PdfPCell(new Phrase(cdBody_OriginalAmount_Currency_text));
                            PdfPCell cdBody_OriginalAmount_ExclTax_cell = new PdfPCell(new Phrase(cdBody_OriginalAmount_ExclTax_text));
                            PdfPCell cdBody_OriginalAmount_Tax_cell = new PdfPCell(new Phrase(cdBody_OriginalAmount_Tax_text));
                            PdfPCell cdBody_OriginalAmount_InclTax_cell = new PdfPCell(new Phrase(cdBody_OriginalAmount_InclTax_text));
                            PdfPCell cdBody_ExchangeRate_cell = new PdfPCell(new Phrase(cdBody_ExchangeRate_text));
                            PdfPCell cdBody_ClaimCurrency_cell = new PdfPCell(new Phrase(cdBody_ClaimCurrency_text));
                            PdfPCell cdBody_TotalClaimAmount_cell = new PdfPCell(new Phrase(cdBody_TotalClaimAmount_text));
                            //PdfPCell cdBody_Ref_cell = new PdfPCell(new Phrase(cdBody_Ref_text));
                            PdfPCell cdBody_Remark_cell = new PdfPCell(new Phrase(cdBody_Remark_text));

                            cdBody_CostType_cell.HorizontalAlignment = Element.ALIGN_LEFT;
                            cdBody_CostType_cell.VerticalAlignment = Element.ALIGN_TOP;
                            cdBody_VoucherDate_cell.HorizontalAlignment = Element.ALIGN_LEFT;
                            cdBody_VoucherDate_cell.VerticalAlignment = Element.ALIGN_TOP;
                            cdBody_OriginalAmount_Currency_cell.HorizontalAlignment = Element.ALIGN_LEFT;
                            cdBody_OriginalAmount_Currency_cell.VerticalAlignment = Element.ALIGN_TOP;
                            cdBody_OriginalAmount_Currency_cell.Border = Rectangle.TOP_BORDER | Rectangle.LEFT_BORDER | Rectangle.BOTTOM_BORDER;
                            cdBody_OriginalAmount_ExclTax_cell.HorizontalAlignment = Element.ALIGN_RIGHT;
                            cdBody_OriginalAmount_ExclTax_cell.VerticalAlignment = Element.ALIGN_TOP;
                            cdBody_OriginalAmount_ExclTax_cell.Border = Rectangle.TOP_BORDER | Rectangle.RIGHT_BORDER | Rectangle.BOTTOM_BORDER;
                            cdBody_OriginalAmount_Tax_cell.HorizontalAlignment = Element.ALIGN_RIGHT;
                            cdBody_OriginalAmount_Tax_cell.VerticalAlignment = Element.ALIGN_TOP;
                            cdBody_OriginalAmount_Tax_cell.Border = Rectangle.TOP_BORDER | Rectangle.RIGHT_BORDER | Rectangle.BOTTOM_BORDER;
                            cdBody_OriginalAmount_InclTax_cell.HorizontalAlignment = Element.ALIGN_RIGHT;
                            cdBody_OriginalAmount_InclTax_cell.VerticalAlignment = Element.ALIGN_TOP;
                            cdBody_OriginalAmount_InclTax_cell.Border = Rectangle.TOP_BORDER | Rectangle.RIGHT_BORDER | Rectangle.BOTTOM_BORDER;
                            cdBody_ExchangeRate_cell.HorizontalAlignment = Element.ALIGN_RIGHT;
                            cdBody_ExchangeRate_cell.VerticalAlignment = Element.ALIGN_TOP;
                            cdBody_ClaimCurrency_cell.HorizontalAlignment = Element.ALIGN_LEFT;
                            cdBody_ClaimCurrency_cell.VerticalAlignment = Element.ALIGN_TOP;
                            cdBody_ClaimCurrency_cell.Border = Rectangle.TOP_BORDER | Rectangle.LEFT_BORDER | Rectangle.BOTTOM_BORDER;
                            cdBody_TotalClaimAmount_cell.HorizontalAlignment = Element.ALIGN_RIGHT;
                            cdBody_TotalClaimAmount_cell.VerticalAlignment = Element.ALIGN_TOP;
                            cdBody_TotalClaimAmount_cell.Border = Rectangle.TOP_BORDER | Rectangle.RIGHT_BORDER | Rectangle.BOTTOM_BORDER;
                            //cdBody_Ref_cell.HorizontalAlignment = Element.ALIGN_RIGHT;
                            //cdBody_Ref_cell.VerticalAlignment = Element.ALIGN_TOP;
                            cdBody_Remark_cell.HorizontalAlignment = Element.ALIGN_LEFT;
                            cdBody_Remark_cell.VerticalAlignment = Element.ALIGN_TOP;

                            PdfPTable details_table = null;
                            details_table = new PdfPTable(new float[] { 1 });

                            PdfPCell cdBody_Details_Location_cell = new PdfPCell(cdBody_Details_Location_Phrase);
                            PdfPCell cdBody_Details_Date_cell = new PdfPCell(cdBody_Details_Date_Phrase);
                            PdfPCell cdBody_Details_Transportation_cell = new PdfPCell(cdBody_Details_Transportation_Phrase);
                            PdfPCell cdBody_Details_Desc_cell = new PdfPCell(cdBody_Details_Description_Phrase);
                            //PdfPCell cdBody_Details_Remarks_cell = new PdfPCell(cdBody_Details_RemarksTitle_Phrase);

                            cdBody_Details_Location_cell.HorizontalAlignment = Element.ALIGN_LEFT;
                            cdBody_Details_Location_cell.VerticalAlignment = Element.ALIGN_TOP;
                            cdBody_Details_Location_cell.Border = Rectangle.NO_BORDER;
                            cdBody_Details_Date_cell.HorizontalAlignment = Element.ALIGN_LEFT;
                            cdBody_Details_Date_cell.VerticalAlignment = Element.ALIGN_TOP;
                            cdBody_Details_Date_cell.Border = Rectangle.NO_BORDER;
                            cdBody_Details_Transportation_cell.HorizontalAlignment = Element.ALIGN_LEFT;
                            cdBody_Details_Transportation_cell.VerticalAlignment = Element.ALIGN_TOP;
                            cdBody_Details_Transportation_cell.Border = Rectangle.NO_BORDER;
                            cdBody_Details_Desc_cell.HorizontalAlignment = Element.ALIGN_LEFT;
                            cdBody_Details_Desc_cell.VerticalAlignment = Element.ALIGN_TOP;
                            cdBody_Details_Desc_cell.Border = Rectangle.NO_BORDER;
                            //cdBody_Details_Remarks_cell.HorizontalAlignment = Element.ALIGN_LEFT;
                            //cdBody_Details_Remarks_cell.VerticalAlignment = Element.ALIGN_TOP;
                            //cdBody_Details_Remarks_cell.Border = Rectangle.NO_BORDER;

                            if (!(string.IsNullOrWhiteSpace(_details_LocFrom) || string.IsNullOrWhiteSpace(_details_LocTo)))
                                details_table.AddCell(cdBody_Details_Location_cell);

                            if (!(string.IsNullOrWhiteSpace(_details_DateFrom) || string.IsNullOrWhiteSpace(_details_DateTo)))
                                details_table.AddCell(cdBody_Details_Date_cell);

                            if (!string.IsNullOrWhiteSpace(_details_TransType))
                                details_table.AddCell(cdBody_Details_Transportation_cell);

                            if (!string.IsNullOrWhiteSpace(_details_desc))
                                details_table.AddCell(cdBody_Details_Desc_cell);

                            //if (!string.IsNullOrWhiteSpace(_details_remarks))
                            //    details_table.AddCell(cdBody_Details_Remarks_cell);

                            PdfPCell cdBody_Details_cell = new PdfPCell(details_table);

                            claimDetails_table.AddCell(cdBody_CostType_cell);
                            claimDetails_table.AddCell(cdBody_VoucherDate_cell);

                            claimDetails_table.AddCell(cdBody_Details_cell);

                            claimDetails_table.AddCell(cdBody_OriginalAmount_Currency_cell);
                            claimDetails_table.AddCell(cdBody_OriginalAmount_ExclTax_cell);
                            claimDetails_table.AddCell(cdBody_OriginalAmount_Currency_cell);
                            claimDetails_table.AddCell(cdBody_OriginalAmount_Tax_cell);
                            claimDetails_table.AddCell(cdBody_OriginalAmount_Currency_cell);
                            claimDetails_table.AddCell(cdBody_OriginalAmount_InclTax_cell);

                            claimDetails_table.AddCell(cdBody_ExchangeRate_cell);
                            claimDetails_table.AddCell(cdBody_ClaimCurrency_cell);
                            claimDetails_table.AddCell(cdBody_TotalClaimAmount_cell);
                            //claimDetails_table.AddCell(cdBody_Ref_cell);
                            claimDetails_table.AddCell(cdBody_Remark_cell);
                        }

                        Chunk cdBody_GrandTotal_text = new Chunk(Localization.GetString("pdfGrandTotal", LocalResourceFile), content_bold);
                        PdfPCell cdBody_GrandTotal_cell = new PdfPCell(new Phrase(cdBody_GrandTotal_text));

                        cdBody_GrandTotal_cell.Colspan = 3;
                        cdBody_GrandTotal_cell.HorizontalAlignment = Element.ALIGN_RIGHT;
                        cdBody_GrandTotal_cell.VerticalAlignment = Element.ALIGN_TOP;

                        PdfPTable cdBody_ExclTaxTotal_table = null;
                        cdBody_ExclTaxTotal_table = new PdfPTable(new float[] { 1, 2 });
                        PdfPTable cdBody_TaxTotal_table = null;
                        cdBody_TaxTotal_table = new PdfPTable(new float[] { 1, 2 });
                        PdfPTable cdBody_InclTaxTotal_table = null;
                        cdBody_InclTaxTotal_table = new PdfPTable(new float[] { 1, 2 });
                        PdfPTable cdBody_ClaimTotal_table = null;
                        cdBody_ClaimTotal_table = new PdfPTable(new float[] { 1, 2 });

                        foreach (DictionaryEntry _ex in ExclTaxTotal)
                        {
                            Chunk cdBody_ExclTaxTotalCurrency_text = new Chunk(Convert.ToString(_ex.Key), content_bold);
                            Chunk cdBody_ExclTaxTotal_text = new Chunk(Convert.ToString(_ex.Value), content_bold);
                            PdfPCell cdBody_ExclTaxTotalCurrency_cell_temp = new PdfPCell(new Phrase(cdBody_ExclTaxTotalCurrency_text));
                            PdfPCell cdBody_ExclTaxTotal_cell_temp = new PdfPCell(new Phrase(cdBody_ExclTaxTotal_text));

                            cdBody_ExclTaxTotalCurrency_cell_temp.Border = Rectangle.NO_BORDER;
                            cdBody_ExclTaxTotalCurrency_cell_temp.HorizontalAlignment = Element.ALIGN_LEFT;
                            cdBody_ExclTaxTotalCurrency_cell_temp.VerticalAlignment = Element.ALIGN_TOP;

                            cdBody_ExclTaxTotal_cell_temp.Border = Rectangle.NO_BORDER;
                            cdBody_ExclTaxTotal_cell_temp.HorizontalAlignment = Element.ALIGN_RIGHT;
                            cdBody_ExclTaxTotal_cell_temp.VerticalAlignment = Element.ALIGN_TOP;

                            cdBody_ExclTaxTotal_table.AddCell(cdBody_ExclTaxTotalCurrency_cell_temp);
                            cdBody_ExclTaxTotal_table.AddCell(cdBody_ExclTaxTotal_cell_temp);

                            foreach (DictionaryEntry _t in TaxTotal)
                            {
                                if (_t.Key.Equals(_ex.Key))
                                {
                                    Chunk cdBody_TaxTotalCurrency_text = new Chunk(Convert.ToString(_t.Key), content_bold);
                                    Chunk cdBody_TaxTotal_text = new Chunk(Convert.ToString(_t.Value), content_bold);
                                    PdfPCell cdBody_TaxTotalCurrency_cell_temp = new PdfPCell(new Phrase(cdBody_TaxTotalCurrency_text));
                                    PdfPCell cdBody_TaxTotal_cell_temp = new PdfPCell(new Phrase(cdBody_TaxTotal_text));

                                    cdBody_TaxTotalCurrency_cell_temp.Border = Rectangle.NO_BORDER;
                                    cdBody_TaxTotalCurrency_cell_temp.HorizontalAlignment = Element.ALIGN_LEFT;
                                    cdBody_TaxTotalCurrency_cell_temp.VerticalAlignment = Element.ALIGN_TOP;

                                    cdBody_TaxTotal_cell_temp.Border = Rectangle.NO_BORDER;
                                    cdBody_TaxTotal_cell_temp.HorizontalAlignment = Element.ALIGN_RIGHT;
                                    cdBody_TaxTotal_cell_temp.VerticalAlignment = Element.ALIGN_TOP;

                                    cdBody_TaxTotal_table.AddCell(cdBody_TaxTotalCurrency_cell_temp);
                                    cdBody_TaxTotal_table.AddCell(cdBody_TaxTotal_cell_temp);

                                    break;
                                }
                            }

                            foreach (DictionaryEntry _in in InclTaxTotal)
                            {
                                if (_in.Key.Equals(_ex.Key))
                                {
                                    Chunk cdBody_InclTaxTotalCurrency_text = new Chunk(Convert.ToString(_in.Key), content_bold);
                                    Chunk cdBody_InclTaxTotal_text = new Chunk(Convert.ToString(_in.Value), content_bold);
                                    PdfPCell cdBody_InclTaxTotalCurrency_cell_temp = new PdfPCell(new Phrase(cdBody_InclTaxTotalCurrency_text));
                                    PdfPCell cdBody_InclTaxTotal_cell_temp = new PdfPCell(new Phrase(cdBody_InclTaxTotal_text));

                                    cdBody_InclTaxTotalCurrency_cell_temp.Border = Rectangle.NO_BORDER;
                                    cdBody_InclTaxTotalCurrency_cell_temp.HorizontalAlignment = Element.ALIGN_LEFT;
                                    cdBody_InclTaxTotalCurrency_cell_temp.VerticalAlignment = Element.ALIGN_TOP;

                                    cdBody_InclTaxTotal_cell_temp.Border = Rectangle.NO_BORDER;
                                    cdBody_InclTaxTotal_cell_temp.HorizontalAlignment = Element.ALIGN_RIGHT;
                                    cdBody_InclTaxTotal_cell_temp.VerticalAlignment = Element.ALIGN_TOP;

                                    cdBody_InclTaxTotal_table.AddCell(cdBody_InclTaxTotalCurrency_cell_temp);
                                    cdBody_InclTaxTotal_table.AddCell(cdBody_InclTaxTotal_cell_temp);
                                    break;
                                }
                            }
                            foreach (DictionaryEntry _ct in ClaimTotal)
                            {
                                if (_ct.Key.Equals(_ex.Key))
                                {
                                    Chunk cdBody_ClaimTotalCurrency_text = new Chunk(Convert.ToString(_ct.Key), content_bold);
                                    Chunk cdBody_ClaimTotal_text = new Chunk(Convert.ToString(_ct.Value), content_bold);
                                    PdfPCell cdBody_ClaimTotalCurrency_cell_temp = new PdfPCell(new Phrase(cdBody_ClaimTotalCurrency_text));
                                    PdfPCell cdBody_ClaimTotal_cell_temp = new PdfPCell(new Phrase(cdBody_ClaimTotal_text));

                                    cdBody_ClaimTotalCurrency_cell_temp.Border = Rectangle.NO_BORDER;
                                    cdBody_ClaimTotalCurrency_cell_temp.HorizontalAlignment = Element.ALIGN_LEFT;
                                    cdBody_ClaimTotalCurrency_cell_temp.VerticalAlignment = Element.ALIGN_TOP;

                                    cdBody_ClaimTotal_cell_temp.Border = Rectangle.NO_BORDER;
                                    cdBody_ClaimTotal_cell_temp.HorizontalAlignment = Element.ALIGN_RIGHT;
                                    cdBody_ClaimTotal_cell_temp.VerticalAlignment = Element.ALIGN_TOP;

                                    cdBody_ClaimTotal_table.AddCell(cdBody_ClaimTotalCurrency_cell_temp);
                                    cdBody_ClaimTotal_table.AddCell(cdBody_ClaimTotal_cell_temp);

                                    ClaimTotal.Remove(_ct.Key);
                                    break;
                                }
                            }
                        }

                        foreach (DictionaryEntry _ct in ClaimTotal)
                        {
                            Chunk cdBody_ClaimTotalCurrency_text = new Chunk(Convert.ToString(_ct.Key), content_bold);
                            Chunk cdBody_ClaimTotal_text = new Chunk(Convert.ToString(_ct.Value), content_bold);
                            PdfPCell cdBody_ClaimTotalCurrency_cell_temp = new PdfPCell(new Phrase(cdBody_ClaimTotalCurrency_text));
                            PdfPCell cdBody_ClaimTotal_cell_temp = new PdfPCell(new Phrase(cdBody_ClaimTotal_text));

                            cdBody_ClaimTotalCurrency_cell_temp.Border = Rectangle.NO_BORDER;
                            cdBody_ClaimTotalCurrency_cell_temp.HorizontalAlignment = Element.ALIGN_LEFT;
                            cdBody_ClaimTotalCurrency_cell_temp.VerticalAlignment = Element.ALIGN_TOP;

                            cdBody_ClaimTotal_cell_temp.Border = Rectangle.NO_BORDER;
                            cdBody_ClaimTotal_cell_temp.HorizontalAlignment = Element.ALIGN_RIGHT;
                            cdBody_ClaimTotal_cell_temp.VerticalAlignment = Element.ALIGN_TOP;

                            cdBody_ClaimTotal_table.AddCell(cdBody_ClaimTotalCurrency_cell_temp);
                            cdBody_ClaimTotal_table.AddCell(cdBody_ClaimTotal_cell_temp);
                        }

                        PdfPCell cdBody_ExclTaxTotal_cell = new PdfPCell(cdBody_ExclTaxTotal_table);
                        PdfPCell cdBody_TaxTotal_cell = new PdfPCell(cdBody_TaxTotal_table);
                        PdfPCell cdBody_InclTaxTotal_cell = new PdfPCell(cdBody_InclTaxTotal_table);
                        PdfPCell cdBody_ClaimTotal_cell = new PdfPCell(cdBody_ClaimTotal_table);
                        PdfPCell cdBody_Empty_cell = new PdfPCell(new Phrase(new Chunk(string.Empty, content)));
                        PdfPCell cdBody_AllEmpty_cell = new PdfPCell(new Phrase(new Chunk(string.Empty, content)));

                        cdBody_ExclTaxTotal_cell.Colspan = 2;
                        cdBody_TaxTotal_cell.Colspan = 2;
                        cdBody_InclTaxTotal_cell.Colspan = 2;
                        cdBody_ClaimTotal_cell.Colspan = 2;
                        cdBody_ExclTaxTotal_cell.HorizontalAlignment = Element.ALIGN_RIGHT;
                        cdBody_TaxTotal_cell.HorizontalAlignment = Element.ALIGN_RIGHT;
                        cdBody_InclTaxTotal_cell.HorizontalAlignment = Element.ALIGN_RIGHT;
                        cdBody_ClaimTotal_cell.HorizontalAlignment = Element.ALIGN_RIGHT;
                        cdBody_ExclTaxTotal_cell.VerticalAlignment = Element.ALIGN_TOP;
                        cdBody_TaxTotal_cell.VerticalAlignment = Element.ALIGN_TOP;
                        cdBody_InclTaxTotal_cell.VerticalAlignment = Element.ALIGN_TOP;
                        cdBody_ClaimTotal_cell.VerticalAlignment = Element.ALIGN_TOP;

                        cdBody_AllEmpty_cell.FixedHeight = 2;
                        cdBody_AllEmpty_cell.Colspan = 13;

                        claimDetails_table.AddCell(cdBody_AllEmpty_cell);

                        claimDetails_table.AddCell(cdBody_GrandTotal_cell);
                        claimDetails_table.AddCell(cdBody_ExclTaxTotal_cell);
                        claimDetails_table.AddCell(cdBody_TaxTotal_cell);
                        claimDetails_table.AddCell(cdBody_InclTaxTotal_cell);
                        claimDetails_table.AddCell(cdBody_Empty_cell);
                        claimDetails_table.AddCell(cdBody_ClaimTotal_cell);
                        claimDetails_table.AddCell(cdBody_Empty_cell);

                        Paragraph body = new Paragraph();

                        body.Alignment = Element.ALIGN_JUSTIFIED;

                        body.Add(claimDetails_table);
                        body.Add(Environment.NewLine);

                        #endregion

                        #region PDF Footer                        

                        Paragraph footer = new Paragraph();

                        PdfPTable footer_table = null;
                        footer_table = new PdfPTable(new float[] { 3, 2, 6, 1, 2, 1, 3, 1, 2, 1, 1, 2, 1 });
                        footer_table.WidthPercentage = 100;

                        Chunk cdFooter_Note_text = new Chunk(Localization.GetString("pdfNote", LocalResourceFile), content_bold_underline);
                        PdfPCell cdFooter_Note_cell = new PdfPCell(new Phrase(cdFooter_Note_text));
                        Chunk cdFooter_EmployeeSignature_text = new Chunk(Localization.GetString("pdfEmployeeSignature", LocalResourceFile), content_bold);
                        PdfPCell cdFooter_EmployeeSignature_cell = new PdfPCell(new Phrase(cdFooter_EmployeeSignature_text));
                        Chunk cdFooter_Approval_text = new Chunk(Localization.GetString("pdfApproval", LocalResourceFile), content_bold);
                        PdfPCell cdFooter_Approval_cell = new PdfPCell(new Phrase(cdFooter_Approval_text));
                        Chunk cdFooter_VerifiedByAC_text = new Chunk(Localization.GetString("pdfVerifiedByAC", LocalResourceFile), content_bold);
                        PdfPCell cdFooter_VerifiedByAC_cell = new PdfPCell(new Phrase(cdFooter_VerifiedByAC_text));
                        Chunk cdFooter_GMApproval_text = new Chunk(Localization.GetString("pdfGMApproval", LocalResourceFile), content_bold);
                        PdfPCell cdFooter_GMApproval_cell = new PdfPCell(new Phrase(cdFooter_GMApproval_text));

                        Chunk cdFooter_NoteContent1_text = new Chunk(Localization.GetString("pdfNoteContent1", LocalResourceFile), content_small);
                        PdfPCell cdFooter_NoteContent1_cell = new PdfPCell(new Phrase(cdFooter_NoteContent1_text));
                        Chunk cdFooter_NoteContent2_text = new Chunk(Localization.GetString("pdfNoteContent2", LocalResourceFile), content_small);
                        PdfPCell cdFooter_NoteContent2_cell = new PdfPCell(new Phrase(cdFooter_NoteContent2_text));
                        Chunk cdFooter_NoteContent3_text = new Chunk(Localization.GetString("pdfNoteContent3", LocalResourceFile), content_small);
                        PdfPCell cdFooter_NoteContent3_cell = new PdfPCell(new Phrase(cdFooter_NoteContent3_text));
                        Chunk cdFooter_NoteContent4_text = new Chunk(Localization.GetString("pdfNoteContent4", LocalResourceFile), content_small);
                        PdfPCell cdFooter_NoteContent4_cell = new PdfPCell(new Phrase(cdFooter_NoteContent4_text));

                        Chunk cdFooter_Date_text = new Chunk(Localization.GetString("pdfFooterDate", LocalResourceFile), content_small);

                        PdfPCell cdFooter_Date1_cell = new PdfPCell(new Phrase(cdFooter_Date_text));
                        PdfPCell cdFooter_Date2_cell = new PdfPCell(new Phrase(cdFooter_Date_text));
                        PdfPCell cdFooter_Date3_cell = new PdfPCell(new Phrase(cdFooter_Date_text));
                        PdfPCell cdFooter_Date4_cell = new PdfPCell(new Phrase(cdFooter_Date_text));

                        PdfPCell cdFooter_NoteEmpty_cell = new PdfPCell(new Phrase(new Chunk(string.Empty)));
                        PdfPCell cdFooter_Date1Empty_cell = new PdfPCell(new Phrase(new Chunk(string.Empty)));
                        PdfPCell cdFooter_Date2Empty_cell = new PdfPCell(new Phrase(new Chunk(string.Empty)));
                        PdfPCell cdFooter_Date3Empty_cell = new PdfPCell(new Phrase(new Chunk(string.Empty)));
                        PdfPCell cdFooter_Date4Empty_cell = new PdfPCell(new Phrase(new Chunk(string.Empty)));

                        cdFooter_Note_cell.HorizontalAlignment = Element.ALIGN_LEFT;
                        cdFooter_EmployeeSignature_cell.HorizontalAlignment = Element.ALIGN_LEFT;
                        cdFooter_Approval_cell.HorizontalAlignment = Element.ALIGN_LEFT;
                        cdFooter_VerifiedByAC_cell.HorizontalAlignment = Element.ALIGN_LEFT;
                        cdFooter_GMApproval_cell.HorizontalAlignment = Element.ALIGN_LEFT;
                        cdFooter_NoteContent1_cell.HorizontalAlignment = Element.ALIGN_LEFT;
                        cdFooter_NoteContent2_cell.HorizontalAlignment = Element.ALIGN_LEFT;
                        cdFooter_NoteContent3_cell.HorizontalAlignment = Element.ALIGN_LEFT;
                        cdFooter_NoteContent4_cell.HorizontalAlignment = Element.ALIGN_LEFT;
                        cdFooter_Date1_cell.HorizontalAlignment = Element.ALIGN_LEFT;
                        cdFooter_Date2_cell.HorizontalAlignment = Element.ALIGN_LEFT;
                        cdFooter_Date3_cell.HorizontalAlignment = Element.ALIGN_LEFT;
                        cdFooter_Date4_cell.HorizontalAlignment = Element.ALIGN_LEFT;

                        cdFooter_Note_cell.VerticalAlignment = Element.ALIGN_TOP;
                        cdFooter_EmployeeSignature_cell.VerticalAlignment = Element.ALIGN_TOP;
                        cdFooter_Approval_cell.VerticalAlignment = Element.ALIGN_TOP;
                        cdFooter_VerifiedByAC_cell.VerticalAlignment = Element.ALIGN_TOP;
                        cdFooter_GMApproval_cell.VerticalAlignment = Element.ALIGN_TOP;
                        cdFooter_NoteContent1_cell.VerticalAlignment = Element.ALIGN_TOP;
                        cdFooter_NoteContent2_cell.VerticalAlignment = Element.ALIGN_TOP;
                        cdFooter_NoteContent3_cell.VerticalAlignment = Element.ALIGN_TOP;
                        cdFooter_NoteContent4_cell.VerticalAlignment = Element.ALIGN_TOP;
                        cdFooter_Date1_cell.VerticalAlignment = Element.ALIGN_TOP;
                        cdFooter_Date2_cell.VerticalAlignment = Element.ALIGN_TOP;
                        cdFooter_Date3_cell.VerticalAlignment = Element.ALIGN_TOP;
                        cdFooter_Date4_cell.VerticalAlignment = Element.ALIGN_TOP;

                        cdFooter_Note_cell.BorderWidthTop = 1.5f;
                        cdFooter_Note_cell.BorderWidthLeft = 1.5f;
                        cdFooter_Note_cell.Colspan = 3;
                        cdFooter_Note_cell.Border = Rectangle.LEFT_BORDER | Rectangle.TOP_BORDER;

                        cdFooter_EmployeeSignature_cell.BorderWidthTop = 1.5f;
                        cdFooter_EmployeeSignature_cell.Colspan = 2;
                        cdFooter_EmployeeSignature_cell.Rowspan = 4;
                        cdFooter_EmployeeSignature_cell.Border = Rectangle.LEFT_BORDER | Rectangle.TOP_BORDER;

                        cdFooter_Approval_cell.BorderWidthTop = 1.5f;
                        cdFooter_Approval_cell.Colspan = 2;
                        cdFooter_Approval_cell.Rowspan = 4;
                        cdFooter_Approval_cell.Border = Rectangle.LEFT_BORDER | Rectangle.TOP_BORDER;

                        cdFooter_VerifiedByAC_cell.BorderWidthTop = 1.5f;
                        cdFooter_VerifiedByAC_cell.Colspan = 3;
                        cdFooter_VerifiedByAC_cell.Rowspan = 4;
                        cdFooter_VerifiedByAC_cell.Border = Rectangle.LEFT_BORDER | Rectangle.TOP_BORDER;

                        cdFooter_GMApproval_cell.BorderWidthTop = 1.5f;
                        cdFooter_GMApproval_cell.BorderWidthRight = 1.5f;
                        cdFooter_GMApproval_cell.Colspan = 3;
                        cdFooter_GMApproval_cell.Rowspan = 4;
                        cdFooter_GMApproval_cell.Border = Rectangle.LEFT_BORDER | Rectangle.TOP_BORDER | Rectangle.RIGHT_BORDER;

                        cdFooter_NoteContent1_cell.Colspan = 3;
                        cdFooter_NoteContent1_cell.BorderWidthLeft = 1.5f;
                        cdFooter_NoteContent1_cell.Border = Rectangle.LEFT_BORDER;

                        cdFooter_NoteContent2_cell.Colspan = 3;
                        cdFooter_NoteContent2_cell.BorderWidthLeft = 1.5f;
                        cdFooter_NoteContent2_cell.Border = Rectangle.LEFT_BORDER;

                        cdFooter_NoteContent3_cell.Colspan = 3;
                        cdFooter_NoteContent3_cell.BorderWidthLeft = 1.5f;
                        cdFooter_NoteContent3_cell.Border = Rectangle.LEFT_BORDER;

                        cdFooter_NoteContent4_cell.Colspan = 3;
                        cdFooter_NoteContent4_cell.BorderWidthLeft = 1.5f;
                        
                        cdFooter_NoteContent4_cell.Border = Rectangle.LEFT_BORDER;

                        cdFooter_Date1_cell.Colspan = 2;
                        cdFooter_Date2_cell.Colspan = 2;
                        cdFooter_Date3_cell.Colspan = 3;
                        cdFooter_Date4_cell.Colspan = 3;

                        cdFooter_Date1_cell.Border = Rectangle.LEFT_BORDER;
                        cdFooter_Date2_cell.Border = Rectangle.LEFT_BORDER;
                        cdFooter_Date3_cell.Border = Rectangle.LEFT_BORDER;
                        cdFooter_Date4_cell.Border = Rectangle.LEFT_BORDER | Rectangle.RIGHT_BORDER;

                        cdFooter_NoteEmpty_cell.Colspan = 3;
                        cdFooter_Date1Empty_cell.Colspan = 2;
                        cdFooter_Date2Empty_cell.Colspan = 2;
                        cdFooter_Date3Empty_cell.Colspan = 3;
                        cdFooter_Date4Empty_cell.Colspan = 3;

                        cdFooter_Date4_cell.BorderWidthRight = 1.5f;

                        cdFooter_NoteEmpty_cell.Border = Rectangle.LEFT_BORDER | Rectangle.BOTTOM_BORDER;
                        cdFooter_Date1Empty_cell.Border = Rectangle.LEFT_BORDER | Rectangle.BOTTOM_BORDER;
                        cdFooter_Date2Empty_cell.Border = Rectangle.LEFT_BORDER | Rectangle.BOTTOM_BORDER;
                        cdFooter_Date3Empty_cell.Border = Rectangle.LEFT_BORDER | Rectangle.BOTTOM_BORDER;
                        cdFooter_Date4Empty_cell.Border = Rectangle.LEFT_BORDER | Rectangle.RIGHT_BORDER | Rectangle.BOTTOM_BORDER;

                        cdFooter_NoteEmpty_cell.BorderWidthLeft = 1.5f;
                        cdFooter_NoteEmpty_cell.BorderWidthBottom = 1.5f;
                        cdFooter_Date1Empty_cell.BorderWidthBottom = 1.5f;
                        cdFooter_Date2Empty_cell.BorderWidthBottom = 1.5f;
                        cdFooter_Date3Empty_cell.BorderWidthBottom = 1.5f;
                        cdFooter_Date4Empty_cell.BorderWidthBottom = 1.5f;
                        cdFooter_Date4Empty_cell.BorderWidthRight = 1.5f;
                        cdFooter_NoteEmpty_cell.FixedHeight = 1;
                        cdFooter_Date1Empty_cell.FixedHeight = 1;
                        cdFooter_Date2Empty_cell.FixedHeight = 1;
                        cdFooter_Date3Empty_cell.FixedHeight = 1;
                        cdFooter_Date4Empty_cell.FixedHeight = 1;

                        footer_table.AddCell(cdFooter_Note_cell);
                        footer_table.AddCell(cdFooter_EmployeeSignature_cell);
                        footer_table.AddCell(cdFooter_Approval_cell);
                        footer_table.AddCell(cdFooter_VerifiedByAC_cell);
                        footer_table.AddCell(cdFooter_GMApproval_cell);
                        footer_table.AddCell(cdFooter_NoteContent1_cell);
                        footer_table.AddCell(cdFooter_NoteContent2_cell);
                        footer_table.AddCell(cdFooter_NoteContent3_cell);
                        footer_table.AddCell(cdFooter_NoteContent4_cell);
                        footer_table.AddCell(cdFooter_Date1_cell);
                        footer_table.AddCell(cdFooter_Date2_cell);
                        footer_table.AddCell(cdFooter_Date3_cell);
                        footer_table.AddCell(cdFooter_Date4_cell);
                        footer_table.AddCell(cdFooter_NoteEmpty_cell);
                        footer_table.AddCell(cdFooter_Date1Empty_cell);
                        footer_table.AddCell(cdFooter_Date2Empty_cell);
                        footer_table.AddCell(cdFooter_Date3Empty_cell);
                        footer_table.AddCell(cdFooter_Date4Empty_cell);
                        footer_table.KeepTogether= true;
                        footer.Alignment = Element.ALIGN_JUSTIFIED;
                        footer.Add(footer_table);

                        #endregion

                        doc.Add(header);
                        doc.Add(body);
                        doc.Add(footer);

                        doc.Close();

                        string filename = string.Format("Expenditure Claim Form {0} {1}.pdf",
                            UserController.GetUserById(PortalId, _ClaimForm.CreatedBy) != null ? UserController.GetUserById(PortalId, _ClaimForm.CreatedBy).DisplayName : string.Empty,
                            DateTime.Now.ToString("yyyyMMdd")
                            );

                        byte[] bytes;
                        using (MemoryStream stream = new MemoryStream())
                        {
                            PdfReader reader = new PdfReader(memory.GetBuffer());
                            using (PdfStamper stamper = new PdfStamper(reader, stream))
                            {
                                int pages = reader.NumberOfPages;
                                for (int i = 1; i <= pages; i++)
                                {
                                    ColumnText.ShowTextAligned(stamper.GetUnderContent(i), Element.ALIGN_RIGHT, new Phrase(string.Format(Localization.GetString("pdfPageNumber", LocalResourceFile), i.ToString(), pages.ToString()), content), 820f, 10f, 0);
                                }
                            }
                            bytes = stream.ToArray();
                        }

                        Response.Clear();
                        Response.AddHeader("Content-Disposition", "attachment;filename=" + filename);
                        Response.ContentType = "application/octet-stream";
                        Response.OutputStream.Write(bytes, 0, bytes.Length);
                        Response.OutputStream.Flush();
                        Response.OutputStream.Close();
                        Response.Flush();
                        Response.End();

                    }
                }
            }
            catch (Exception exc) //Module failed to load
            {
                Exceptions.ProcessModuleLoadException(this, exc);
            }


        }            

        protected void btnBack_Click(object sender, EventArgs e)
        {
            Response.Redirect(Globals.NavigateURL());
        }
    }

    //[Serializable]
    public class TempForm
    {
        public string ID { get; set; }
        public string RefNo { get; set; }
        public string Region { get; set; }
        public string SubmissionDate { get; set; }
        public string BusinessUnit { get; set; }
        public string ClaimFormType { get; set; }
        public string JobNo { get; set; }
        public string ProjectName { get; set; }
    }
    public class TempFormDetails
    {
        public object[] data;
    }

    public class TempFormDetailsData
    {
        public string ClaimFormID { get; set; }
        public string SeqNo { get; set; }
        public string CostTypeID { get; set; }
        public string VoucDate { get; set; }
        public string PeriodFrom { get; set; }
        public string PeriodTo { get; set; }
        public string LocationFrom { get; set; }
        public string LocationTo { get; set; }
        public string TransportationTypeID { get; set; }
        public string Description { get; set; }
        //public string AutoCal { get; set; }
        public string OriginalCurrencyCode { get; set; }
        public string OriginalAmount { get; set; }
        public string TaxAmount { get; set; }
        public string GSTCode { get; set; }
        public string AmountWithTax { get; set; }
        public string ClaimedCurrencyCode { get; set; }
        public string ExchangeRate { get; set; }
        public string TotalAmount { get; set; }
        public string Remarks { get; set; }
        public string IncludeRef { get; set; }
    }
}