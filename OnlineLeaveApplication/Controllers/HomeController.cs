using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using zkemkeeper;

namespace OnlineLeaveApplication.Controllers
{
    public class HomeController : Controller
    {
        OnlineLeaveApplicationEntities db = new OnlineLeaveApplicationEntities();

        public class DashboardLeaveApplication
        {
            public int LeaveApplicationID { get; set; }
            public DateTime? DateAppliedValue { get; set; }
            public DateTime? LastInclusiveDate { get; set; }
            public string DateApplied { get; set; }
            public string LeaveDetail { get; set; }
            public string InclusiveDates { get; set; }
            public int TotalNumberOfDays { get; set; }
            public short? Status { get; set; }
            public string StatusText { get; set; }
            public string ScheduleText { get; set; }
            public string ScheduleBadgeClass { get; set; }
            public string LatestRemarks { get; set; }
            public string LatestRemarksBy { get; set; }
            public string ExpectedActionDate { get; set; }
            public List<DashboardNextStepItem> NextSteps { get; set; }
            public List<DashboardTrackingItem> TrackingItems { get; set; }
        }

        public class DashboardNextStepItem
        {
            public string Title { get; set; }
            public string ExpectedDate { get; set; }
            public bool IsPending { get; set; }
        }

        public class DashboardTrackingItem
        {
            public string DateSubmitted { get; set; }
            public short? Status { get; set; }
            public string StatusText { get; set; }
            public string SubmittedBy { get; set; }
            public string ReceivedBy { get; set; }
            public string Remarks { get; set; }
        }

        private static string GetStatusText(short? status)
        {
            return status == 1 ? "Draft" :
                status == 2 ? "Received" :
                status == 3 ? "For Review" :
                status == 4 ? "For Approval" :
                status == 5 ? "Approved" :
                status == 0 ? "Disapproved" : "";
        }

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
            const string dateFormat = "MMM. d, yyyy";

            if (startDate == endDate)
            {
                return startDate.ToString(dateFormat, System.Globalization.CultureInfo.InvariantCulture);
            }

            return startDate.ToString(dateFormat, System.Globalization.CultureInfo.InvariantCulture) + " - " + endDate.ToString(dateFormat, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string FormatEmployeeName(Employee employee)
        {
            if (employee == null)
            {
                return "";
            }

            return (employee.FirstName + " " + employee.LastName).Trim();
        }

        private static List<DashboardNextStepItem> BuildDashboardNextSteps(short? status, string expectedActionDate)
        {
            var nextStepTitles = new List<string>();

            if (status == 2)
            {
                nextStepTitles.Add("For Review");
                nextStepTitles.Add("For Approval");
                nextStepTitles.Add("Final Decision");
            }
            else if (status == 3)
            {
                nextStepTitles.Add("For Approval");
                nextStepTitles.Add("Final Decision");
            }
            else if (status == 4)
            {
                nextStepTitles.Add("Final Decision");
            }
            else if (status == 5)
            {
                return new List<DashboardNextStepItem>
                {
                    new DashboardNextStepItem
                    {
                        Title = "Application approved",
                        ExpectedDate = "",
                        IsPending = false
                    }
                };
            }
            else if (status == 0)
            {
                return new List<DashboardNextStepItem>
                {
                    new DashboardNextStepItem
                    {
                        Title = "Application disapproved",
                        ExpectedDate = "",
                        IsPending = false
                    }
                };
            }

            return nextStepTitles
                .Select(title => new DashboardNextStepItem
                {
                    Title = title,
                    ExpectedDate = expectedActionDate,
                    IsPending = true
                })
                .ToList();
        }

        private static DashboardLeaveApplication ToDashboardLeaveApplication(LeaveApplication leaveApplication, short? status, DateTime serverDate, Dictionary<short, string> employeeNames)
        {
            var inclusiveDates = leaveApplication.LeaveApplicationDetails
                .SelectMany(detail => detail.LeaveApplicationDetailInclusiveDates)
                .Where(inclusiveDate => inclusiveDate.LeaveDate.HasValue)
                .Select(inclusiveDate => inclusiveDate.LeaveDate.Value.Date)
                .ToList();

            var lastInclusiveDate = inclusiveDates.Any() ? inclusiveDates.Max() : (DateTime?)null;
            var scheduleText = "";
            var scheduleBadgeClass = "badge-secondary";

            if (status == 5)
            {
                if (!lastInclusiveDate.HasValue)
                {
                    scheduleText = "No dates";
                    scheduleBadgeClass = "badge-secondary";
                }
                else if (lastInclusiveDate.Value < serverDate.Date)
                {
                    scheduleText = "Finished";
                    scheduleBadgeClass = "badge-secondary";
                }
                else
                {
                    scheduleText = "Upcoming";
                    scheduleBadgeClass = "badge-info";
                }
            }

            var trackingItems = leaveApplication.LeaveApplicationSubmissions
                .OrderBy(submission => submission.DateSubmitted)
                .ThenBy(submission => submission.LeaveApplicationSubmissionID)
                .Select(submission => new DashboardTrackingItem
                {
                    DateSubmitted = submission.DateSubmitted.HasValue
                        ? submission.DateSubmitted.Value.ToString("MM/dd/yyyy hh:mm tt", System.Globalization.CultureInfo.InvariantCulture)
                        : "",
                    Status = submission.Status,
                    StatusText = GetStatusText(submission.Status),
                    SubmittedBy = submission.SubmittedBy.HasValue && employeeNames.ContainsKey(submission.SubmittedBy.Value)
                        ? employeeNames[submission.SubmittedBy.Value]
                        : "",
                    ReceivedBy = submission.ReceivedBy.HasValue && employeeNames.ContainsKey(submission.ReceivedBy.Value)
                        ? employeeNames[submission.ReceivedBy.Value]
                        : "",
                    Remarks = submission.Remarks ?? ""
                })
                .ToList();
            var latestRemarkItem = trackingItems
                .Where(trackingItem => !string.IsNullOrWhiteSpace(trackingItem.Remarks))
                .LastOrDefault();
            var expectedActionDate = leaveApplication.DateApplied.HasValue
                ? leaveApplication.DateApplied.Value.AddDays(3).ToString("MMM. d, yyyy", System.Globalization.CultureInfo.InvariantCulture)
                : "";

            return new DashboardLeaveApplication
            {
                LeaveApplicationID = leaveApplication.LeaveApplicationID,
                DateAppliedValue = leaveApplication.DateApplied,
                LastInclusiveDate = lastInclusiveDate,
                DateApplied = leaveApplication.DateApplied.HasValue
                    ? leaveApplication.DateApplied.Value.ToString("MM/dd/yyyy hh:mm tt", System.Globalization.CultureInfo.InvariantCulture)
                    : "",
                LeaveDetail = string.Join(", ", leaveApplication.LeaveApplicationDetails
                    .Select(detail => detail.TypeOfLeave.TypeOfLeave1)
                    .ToList()),
                InclusiveDates = FormatInclusiveDates(leaveApplication.LeaveApplicationDetails),
                TotalNumberOfDays = inclusiveDates.Count,
                Status = status,
                StatusText = GetStatusText(status),
                ScheduleText = scheduleText,
                ScheduleBadgeClass = scheduleBadgeClass,
                LatestRemarks = latestRemarkItem != null ? latestRemarkItem.Remarks : "",
                LatestRemarksBy = latestRemarkItem != null ? latestRemarkItem.SubmittedBy : "",
                ExpectedActionDate = expectedActionDate,
                NextSteps = BuildDashboardNextSteps(status, expectedActionDate),
                TrackingItems = trackingItems
            };
        }

        public ActionResult Login(Employee employee) {
            //CZKEM cz = new CZKEM();

            if (employee.Username == null)
            {
                ViewBag.Message = "";
                return View();
            }
            else {
                var obj = db.Employees.Where(a => a.Username == employee.Username && a.Password == employee.Password).FirstOrDefault();
                if (obj == null)
                {
                    ViewBag.Message = "Incorrect username/password";

                    return View();
                }
                else
                {
                    int employeeID = obj.EmployeeID;

                    Session["EmployeeID"] = employeeID;
                    Session["EmployeeName"] = (obj.FirstName + " " + obj.LastName).Trim();
                    //var result = db.MainOffices
                    //    .Where(m => m.MainOfficeID == obj.Office.MainOfficeID)
                    //    .Select(m => new
                    //    {
                    //        ForInitialReview = obj != null && m.ForInitialReview == obj.EmployeeID,
                    //        ForReview = obj != null && m.ForReview == obj.EmployeeID,
                    //        ForApproval = obj != null && m.ForApproval == obj.EmployeeID
                    //    }).FirstOrDefault();
                    var result = db.MainOffices
                                .Where(m => m.MainOfficeID == obj.Office.MainOfficeID)
                                .Select(m => new
                                {
                                    ForInitialReview = m.ForInitialReview == employeeID,
                                    ForReview = m.ForReview == employeeID,
                                    ForApproval = m.ForApproval == employeeID
                                })
                                .FirstOrDefault();
                    Session["Status"] = result.ForInitialReview ? 2 : result.ForReview ? 3 : result.ForApproval ? 4 : 0;

                    return RedirectToAction("Dashboard", "Home");
                }
            }
             
        }

        public ActionResult Logout()
        {
            Session.Clear();
            Session.Abandon();

            return RedirectToAction("Login", "Home");
        }

        public ActionResult Index()
        {
            var i = Environment.Is64BitOperatingSystem;
            if (Convert.ToInt16(Session["EmployeeID"]) == 0)
            {
                return RedirectToAction("Login", "Home");
            }

            // Fetch categories from the database
            var typeOfLeaves = db.TypeOfLeaves
                                     .Select(c => new SelectListItem
                                     {
                                         Value = c.TypeOfLeaveID.ToString(),
                                         Text = c.TypeOfLeave1.ToString()
                                     })
                                     .ToList();

            // Pass the categories to the view using ViewBag or ViewModel
            ViewBag.TypeOfLeaves = typeOfLeaves; 
           var serverDate = db.Database.SqlQuery<DateTime>("SELECT GETDATE()"); 
            ViewBag.disabledDates = db.LeaveApplicationDetailInclusiveDates.Where(a => a.LeaveApplicationDetail.LeaveApplication.EmployeeID == 1).Select(a => a.LeaveDate.ToString()).ToArray();
            return View();
        }
        public ActionResult RegionalOrders()
        {
            if (Session["EmployeeID"] == null)
            {
                return RedirectToAction("Login", "Home");
            }

            ViewBag.Message = "Your application description page.";

            return View();
        }

        public ActionResult RegionalOrderCalendar()
        {
            if (Session["EmployeeID"] == null)
            {
                return RedirectToAction("Login", "Home");
            }

            return View();
        }

        public ActionResult Signatories()
        {
            var signatories = db.Employees
                                     .Select(c => new SelectListItem
                                     {
                                         Value = c.EmployeeID.ToString(),
                                         Text = c.LastName + ", " + c.FirstName
                                     }).ToList();
            ViewBag.Signatories = signatories;
            ViewBag.Message = "Your application description page.";

            return View();
        }
        public ActionResult ManageRegionalOrder()
        {
            if (Session["Status"] == null || Convert.ToInt32(Session["Status"]) == 0)
            {
                return RedirectToAction("RegionalOrders", "Home");
            }

            ViewBag.Message = "Your application description page.";
            // Fetch categories from the database
            var employees = db.Employees
                                     .Select(c => new SelectListItem
                                     {
                                         Value = c.EmployeeID.ToString(),
                                         Text = c.LastName + ", " + c.FirstName + " " + c.MiddleName
                                     })
                                     .ToList();

            // Pass the categories to the view using ViewBag or ViewModel
            ViewBag.Employees = employees;
            return View();
        }

        public ActionResult ListofApplications()
        {
            var employeeID = Convert.ToInt16(Session["EmployeeID"]);
            var obj = db.Employees.Where(a => a.EmployeeID == employeeID).FirstOrDefault();

            //var result = db.MainOffices
            //            .Where(m => m.MainOfficeID == obj.Office.MainOfficeID)
            //            .Select(m => new
            //            {
            //                ForInitialReview = m.ForInitialReview == employeeID,
            //                ForReview = m.ForReview == employeeID,
            //                ForApproval = m.ForApproval == employeeID
            //            }).FirstOrDefault();

            //Session["Status"] = result.ForInitialReview ? 2 : result.ForReview ? 3 : 4;
            return View();
        }

        public ActionResult CTOApplication()
        {
            ViewBag.Message = "Your application description page.";
            ViewBag.CTO = db.RegionalOrders.Count(a => a.isCompensatoryOvertimeCredit == true);
            return View();
        }

        public ActionResult Dashboard()
        {
            if (Session["EmployeeID"] == null)
            {
                return RedirectToAction("Login", "Home");
            }

            var employeeID = Convert.ToInt16(Session["EmployeeID"]);
            var serverDate = db.Database.SqlQuery<DateTime>("SELECT GETDATE()").Single();
            var currentYearApplications = db.LeaveApplications
                .Where(leaveApplication => leaveApplication.EmployeeID == employeeID
                    && leaveApplication.DateApplied.HasValue
                    && leaveApplication.DateApplied.Value.Year == serverDate.Year)
                .ToList();

            var dashboardApplications = currentYearApplications
                .Select(leaveApplication => new
                {
                    LeaveApplication = leaveApplication,
                    LatestStatus = leaveApplication.LeaveApplicationSubmissions
                        .OrderByDescending(submission => submission.DateSubmitted)
                        .ThenByDescending(submission => submission.LeaveApplicationSubmissionID)
                        .FirstOrDefault()?.Status
                })
                .ToList();
            var trackingEmployeeIDs = currentYearApplications
                .SelectMany(leaveApplication => leaveApplication.LeaveApplicationSubmissions)
                .SelectMany(submission => new[] { submission.SubmittedBy, submission.ReceivedBy })
                .Where(employee => employee.HasValue)
                .Select(employee => employee.Value)
                .Distinct()
                .ToList();
            var employeeNames = db.Employees
                .Where(employee => trackingEmployeeIDs.Contains(employee.EmployeeID))
                .ToList()
                .ToDictionary(employee => employee.EmployeeID, employee => FormatEmployeeName(employee));

            ViewBag.ForCertification = dashboardApplications.Count(application => application.LatestStatus == 2);
            ViewBag.ForReview = dashboardApplications.Count(application => application.LatestStatus == 3);
            ViewBag.Approved = dashboardApplications.Count(application => application.LatestStatus == 5);
            ViewBag.Disapproved = dashboardApplications.Count(application => application.LatestStatus == 0);
            ViewBag.InProcessApplications = dashboardApplications
                .Where(application => application.LatestStatus == 2 || application.LatestStatus == 3 || application.LatestStatus == 4)
                .Select(application => ToDashboardLeaveApplication(application.LeaveApplication, application.LatestStatus, serverDate, employeeNames))
                .OrderByDescending(application => application.DateAppliedValue)
                .ToList();
            ViewBag.ApprovedUpcomingApplications = dashboardApplications
                .Where(application => application.LatestStatus == 5)
                .Select(application => ToDashboardLeaveApplication(application.LeaveApplication, application.LatestStatus, serverDate, employeeNames))
                .Where(application => application.ScheduleText != "Finished")
                .OrderBy(application => application.LastInclusiveDate.HasValue ? 0 : 1)
                .ThenBy(application => application.LastInclusiveDate)
                .ToList();
            ViewBag.ApprovedFinishedApplications = dashboardApplications
                .Where(application => application.LatestStatus == 5)
                .Select(application => ToDashboardLeaveApplication(application.LeaveApplication, application.LatestStatus, serverDate, employeeNames))
                .Where(application => application.ScheduleText == "Finished")
                .OrderByDescending(application => application.LastInclusiveDate)
                .ToList();
            ViewBag.DisapprovedApplications = dashboardApplications
                .Where(application => application.LatestStatus == 0)
                .Select(application => ToDashboardLeaveApplication(application.LeaveApplication, application.LatestStatus, serverDate, employeeNames))
                .OrderByDescending(application => application.DateAppliedValue)
                .ToList();
            ViewBag.CurrentYear = serverDate.Year;
            ViewBag.Message = "Your application description page.";

            return View();
        }

        public ActionResult Contact()
        {
            ViewBag.Message = "Your contact page.";

            return View();
        }
    }
}
