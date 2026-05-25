using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace OnlineLeaveApplication.Controllers
{
    public class LeaveApplicationController : Controller
    {
        OnlineLeaveApplicationEntities db = new OnlineLeaveApplicationEntities();

        private static string FormatInclusiveDates(IEnumerable<LeaveApplicationDetail> leaveApplicationDetails)
        {
            var ranges = new List<string>();

            foreach (var leaveApplicationDetail in leaveApplicationDetails)
            {
                var dates = leaveApplicationDetail.LeaveApplicationDetailInclusiveDates
                    .Where(inclusiveDate => inclusiveDate.LeaveDate.HasValue)
                    .Select(inclusiveDate => inclusiveDate.LeaveDate.Value.Date)
                    .Distinct()
                    .OrderBy(leaveDate => leaveDate)
                    .ToList();

                if (!dates.Any())
                {
                    continue;
                }

                var rangeStart = dates.First();
                var previousDate = rangeStart;

                foreach (var leaveDate in dates.Skip(1))
                {
                    if (leaveDate == previousDate.AddDays(1))
                    {
                        previousDate = leaveDate;
                        continue;
                    }

                    ranges.Add(FormatDateRange(rangeStart, previousDate));
                    rangeStart = leaveDate;
                    previousDate = leaveDate;
                }

                ranges.Add(FormatDateRange(rangeStart, previousDate));
            }

            return string.Join("; ", ranges);
        }

        private static string FormatDateRange(DateTime startDate, DateTime endDate)
        {
            var dateFormat = "MMM. d, yyyy";

            if (startDate == endDate)
            {
                return startDate.ToString(dateFormat, CultureInfo.InvariantCulture);
            }

            return startDate.ToString(dateFormat, CultureInfo.InvariantCulture) + " - " + endDate.ToString(dateFormat, CultureInfo.InvariantCulture);
        }

        private static string GetStatusText(short? status)
        {
            return status == 1 ? "Draft" :
                status == 2 ? "For Certification" :
                status == 3 ? "For Review" :
                status == 4 ? "For Approval" :
                status == 5 ? "Approved" :
                status == 0 ? "Disapproved" : "";
        }

        private static short GetNextStatus(short? currentStatus, bool isReturned)
        {
            if (isReturned)
            {
                return 0;
            }

            return currentStatus == 1 ? (short)2 :
                currentStatus == 2 ? (short)3 :
                currentStatus == 3 ? (short)4 :
                currentStatus == 4 ? (short)5 :
                currentStatus == 0 ? (short)2 :
                currentStatus.HasValue ? (short)(currentStatus.Value + 1) : (short)1;
        }

        private void AssignApplicationSignatories(LeaveApplication leaveApplication)
        {
            var employee = db.Employees
                .Include(employeeRecord => employeeRecord.Office.MainOffice)
                .FirstOrDefault(employeeRecord => employeeRecord.EmployeeID == leaveApplication.EmployeeID);

            var mainOffice = employee?.Office?.MainOffice;

            if (mainOffice == null)
            {
                return;
            }

            leaveApplication.ReceivedBy = mainOffice.ForInitialReview;
            leaveApplication.ReviewedBy = mainOffice.ForReview;
            leaveApplication.ApprovedBy = mainOffice.ForApproval;
        }

        // GET: LeaveApplication
        public ActionResult SaveLeaveApplication(List<LeaveApplicationDetail> list)
        {
            if (Session["EmployeeID"] == null)
            {
                return Json("Session expired. Please log in again.", JsonRequestBehavior.AllowGet);
            }

            var employeeID = Convert.ToInt16(Session["EmployeeID"]);
            var serverDate = db.Database.SqlQuery<DateTime>("SELECT GETDATE()").Single();
            LeaveApplication leaveApplication = new LeaveApplication { EmployeeID = employeeID, DateApplied= serverDate };
            leaveApplication.LeaveApplicationDetails = list;
            // Add all leave applications to the DbSet (this is an efficient way to save the list)
            db.LeaveApplications.Add(leaveApplication);

            var leaveApplicationSubmission = new LeaveApplicationSubmission();
            leaveApplicationSubmission.DateSubmitted = serverDate;
            leaveApplicationSubmission.LeaveApplication = leaveApplication;
            leaveApplicationSubmission.SubmittedBy = employeeID;
            leaveApplicationSubmission.Status = 1; // Draft 
            db.LeaveApplicationSubmissions.Add(leaveApplicationSubmission);

            // Save all changes to the database in one transaction
            db.SaveChanges();
            return Json("Successfully saved the leave application record!", JsonRequestBehavior.AllowGet);
        }

        public ActionResult SubmitLeaveApplication(int leaveApplicationID, string remarks, bool isReturned = false)
        {
            if (Session["EmployeeID"] == null)
            {
                return Json("Session expired. Please log in again.", JsonRequestBehavior.AllowGet);
            }

            var employeeID = Convert.ToInt16(Session["EmployeeID"]);

            var leaveApplication = db.LeaveApplications.FirstOrDefault(a => a.LeaveApplicationID == leaveApplicationID);
            var la = db.LeaveApplicationSubmissions
                .Where(a => a.LeaveApplicationID == leaveApplicationID)
                .OrderByDescending(a => a.DateSubmitted)
                .ThenByDescending(a => a.LeaveApplicationSubmissionID)
                .FirstOrDefault();

            if (leaveApplication == null || la == null)
            {
                return Json("Leave application record was not found.", JsonRequestBehavior.AllowGet);
            }

            if (la.Status == 1 && (!leaveApplication.ReceivedBy.HasValue || !leaveApplication.ReviewedBy.HasValue || !leaveApplication.ApprovedBy.HasValue))
            {
                AssignApplicationSignatories(leaveApplication);
            }

            la.ReceivedBy = employeeID;

            //Status = status == "1" ? "Draft" :
            //         status == "2" ? "For Certification" :
            //         status == "3" ? "For Review" :
            //         status == "4" ? "For Approval" :
            //         status == "5" ? "Approved" :
            //         status == "0" ? "Disapproved" : ""
            LeaveApplicationSubmission leaveApplicationSubmission = new LeaveApplicationSubmission();
            var serverDate = db.Database.SqlQuery<DateTime>("SELECT GETDATE()").Single();
            leaveApplicationSubmission.LeaveApplicationID = leaveApplicationID;
            leaveApplicationSubmission.SubmittedBy = employeeID;
            leaveApplicationSubmission.DateSubmitted = serverDate;
            leaveApplicationSubmission.Status = GetNextStatus(la.Status, isReturned);
            leaveApplicationSubmission.Remarks = remarks;
            db.LeaveApplicationSubmissions.Add(leaveApplicationSubmission);
            db.SaveChanges();

            return Json("Successfully submitted leave application record!", JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetLeaveApplications(bool forApproval =false)
        {
            var employeeID = Convert.ToInt16(Session["EmployeeID"]);
            short stats = Convert.ToInt16(Session["Status"]);

            // DataTables parameters
            var draw = Request.Form["draw"];
            var start = int.Parse(Request.Form["start"]);
            var length = int.Parse(Request.Form["length"]);
            var searchValue = Request.Form["search[value]"];

            // Apply search filter if needed
            var filteredData = db.LeaveApplications.Where(a=> a.EmployeeID == employeeID).AsQueryable();// SELECT * FROM Employees
            if (forApproval)
            {
                filteredData = db.LeaveApplications.Where(a => a.LeaveApplicationSubmissions
                           .OrderByDescending(s => s.DateSubmitted)
                           .ThenByDescending(s => s.LeaveApplicationSubmissionID)
                           .FirstOrDefault().Status == stats
                           && ((stats == 2 && a.ReceivedBy == employeeID)
                               || (stats == 3 && a.ReviewedBy == employeeID)
                               || (stats == 4 && a.ApprovedBy == employeeID)));
            }

            if (!string.IsNullOrEmpty(searchValue))
            {
                //filteredData = filteredData.Where(e => e.Lastname.Contains(searchValue) ||
                //                                                 e.Firstname.Contains(searchValue) ||
                //                                                 e.Middlename.Contains(searchValue));
            }
            // Apply ordering (required for Skip to work)
            filteredData = filteredData.OrderByDescending(e => e.DateApplied); // Choose your default column here


            // Total records and records after filtering
            var totalRecords = filteredData.Count();

            // Apply paging
            var data = filteredData.Skip(start).Take(length).ToList();

            var totalRecordsFiltered = filteredData.Count();
             
            object jsonData = data.Select(item =>
            {
                var status = item.LeaveApplicationSubmissions
                    .OrderByDescending(submission => submission.DateSubmitted)
                    .ThenByDescending(submission => submission.LeaveApplicationSubmissionID)
                    .FirstOrDefault()?.Status; // Safe null handling

                return new
                {
                    item.LeaveApplicationID,
                    DateApplied = item.DateApplied.HasValue
                        ? item.DateApplied.Value.ToString("MM/dd/yyyy hh:mm tt", CultureInfo.InvariantCulture)
                        : string.Empty, // Handling potential null values

                    LeaveDetail = string.Join(", ", item.LeaveApplicationDetails
                        .Select(a => a.TypeOfLeave.TypeOfLeave1)
                        .ToList()),

                    LeaveDates = string.Join("<br>", item.LeaveApplicationDetails
                        .Select(b => "<b>" + b.TypeOfLeave.TypeOfLeave1 + " </b>" + "(" + string.Join(", ", b.LeaveApplicationDetailInclusiveDates.Select(c => c.LeaveDate.Value.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture))) + ")")
                        .ToList()),

                    InclusiveDates = FormatInclusiveDates(item.LeaveApplicationDetails),

                    TotalNumberofDays = item.LeaveApplicationDetails
                        .SelectMany(ld => ld.LeaveApplicationDetailInclusiveDates)
                        .Count(),
                        DILGPersonnel = item.Employee.LastName + ", " + item.Employee.FirstName + " " + item.Employee.MiddleName,
                        Attachments = string.Join(", ", item.LeaveApplicationAttachments1
                        .Select(a => a.LeaveApplicationAttachmentID.ToString() + "|" + a.FileName)
                        .ToList()),
                    Status = GetStatusText(status)
                }; 
            }).ToList();

            return Json(new
            {
                draw = draw,
                recordsTotal = totalRecords,
                recordsFiltered = totalRecordsFiltered,
                data = jsonData
            });
        }
        public ActionResult SaveMainOffice(MainOffice mainOffice) {
            if (mainOffice.MainOfficeID != 0)
            {
                //var obj = db.MainOffices.Where(a => a.MainOfficeID == mainOffice.MainOfficeID).FirstOrDefault();
                //obj = mainOffice;
                db.MainOffices.Attach(mainOffice); 
                db.Entry(mainOffice).State = EntityState.Modified;
            }
            else
            {
                db.MainOffices.Add(mainOffice);
            }
            db.SaveChanges();
            return Json("Successfully submitted leave application record!", JsonRequestBehavior.AllowGet);
        }
        public ActionResult GetSignatories()
        {
            // DataTables parameters
            var draw = Request.Form["draw"];
            var start = int.Parse(Request.Form["start"]);
            var length = int.Parse(Request.Form["length"]);
            var searchValue = Request.Form["search[value]"];

            // Apply search filter if needed
            var filteredData = db.MainOffices.AsQueryable();//  

            if (!string.IsNullOrEmpty(searchValue))
            {
                //filteredData = filteredData.Where(e => e.Lastname.Contains(searchValue) ||
                //                                                 e.Firstname.Contains(searchValue) ||
                //                                                 e.Middlename.Contains(searchValue));
            }
            // Apply ordering (required for Skip to work)
            filteredData = filteredData.OrderByDescending(e => e.MainOfficeID); // Choose your default column here
            // Total records and records after filtering
            var totalRecords = filteredData.Count();

            // Apply paging
            var data = filteredData.Skip(start).Take(length).ToList();

            var totalRecordsFiltered = filteredData.Count();

            object jsonData = data.Select(item =>
            {

                return new
                {
                    item.MainOfficeID,
                    item.MainOfficeName,
                    ForCertification = item.Employee==null?"": item.Employee.LastName + ", " + item.Employee.FirstName,
                    ForReview = item.Employee1 == null ? "" : item.Employee1.LastName + ", " + item.Employee1.FirstName,
                    ForApproval = item.Employee2 == null ? "" : item.Employee2.LastName + ", " + item.Employee2.FirstName,
                    ForCertificationID = item.ForInitialReview,
                    ForReviewID = item.ForReview,
                    ForApprovalID = item.ForApproval,
                };
            }).ToList();

            return Json(new
            {
                draw = draw,
                recordsTotal = totalRecords,
                recordsFiltered = totalRecordsFiltered,
                data = jsonData
            });
        }

        public ActionResult DisplayFile(int id)
        {
            var binaryFile = db.LeaveApplicationAttachments.Where(a => a.LeaveApplicationAttachmentID == id).FirstOrDefault();
            if (binaryFile != null)
            {
                return File(binaryFile.UploadedFile, "application/pdf");
            }
            return HttpNotFound();
        }
    }
}
