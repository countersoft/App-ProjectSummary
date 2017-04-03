using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Web.Mvc;
using System.Web.Routing;
using System.Web.UI;
using Countersoft.Foundation.Commons.Extensions;
using Countersoft.Gemini;
using Countersoft.Gemini.Extensibility;
using Countersoft.Gemini.Commons;
using Countersoft.Gemini.Commons.Dto;
using Countersoft.Gemini.Commons.Entity;
using Countersoft.Gemini.Commons.Permissions;
using Countersoft.Gemini.Extensibility.Apps;
using Countersoft.Gemini.Infrastructure;
using Countersoft.Gemini.Infrastructure.Apps;
using System.Linq;
using Countersoft.Gemini.Infrastructure.Helpers;
using ProjectSummary.Data;
using ProjectSummary.Models;

namespace Summary
{
    [AppType(AppTypeEnum.FullPage),
    AppGuid("4880E0A6-7ACD-4D47-AB3D-B414C6C66617"),
    AppControlGuid("6AE44DF8-4637-4284-B058-EEBFB6528E34"),
    AppAuthor("Countersoft"),
    AppKey("Summary"),
    AppName("Project Summary"),
    AppControlUrl("view"),
    AppDescription("Show project summary"),
    AppRequiresViewPermission(true)]
    public class Summary : BaseAppController
    {
        public override WidgetResult Show(IssueDto issue = null)
        {
            WidgetResult result = new WidgetResult();

            List<int> selectedProjects = new List<int>();
            ReportOptions options = new ReportOptions();

            SummaryModel model = new SummaryModel();
            IssuesGridFilter tmp = new IssuesGridFilter();

            try
            {
                if (CurrentCard.IsNew || !CurrentCard.Options.ContainsKey(AppGuid))
                {
                    tmp = new IssuesGridFilter(HttpSessionManager.GetFilter(CurrentCard.Id, CurrentCard.Filter));

                    if (tmp == null)
                    {
                        tmp = CurrentCard.Options[AppGuid].FromJson<IssuesGridFilter>();
                    }

                    if (tmp.Projects == Constants.AllProjectsId.ToString())
                        selectedProjects.Add(Constants.AllProjectsId);
                    else
                        selectedProjects = tmp.GetProjects();
                }
                else
                {
                    options = CurrentCard.Options[AppGuid].FromJson<ReportOptions>();

                    if (options.AllProjectsSelected)
                    {
                        selectedProjects.Add(Constants.AllProjectsId);
                    }
                    else if (options.ProjectIds.Count > 0)
                    {
                        selectedProjects.AddRange(options.ProjectIds);
                    }
                }
               
            }
            catch (Exception ex) 
            {
                tmp = new IssuesGridFilter(HttpSessionManager.GetFilter(CurrentCard.Id, IssuesFilter.CreateProjectFilter(UserContext.User.Entity.Id, UserContext.Project.Entity.Id)));

                selectedProjects = tmp.GetProjects();
            }

            model.ProjectList = GetProjectFilter(selectedProjects);
            model.Options = options;

            result.Markup = new WidgetMarkup("views\\Summary.cshtml", model);

            result.Success = true;

            return result;
        }

        public override WidgetResult Caption(IssueDto issue = null)
        {
            WidgetResult result = new WidgetResult();

            result.Success = true;

            result.Markup.Html = AppName;

            return result;
        }

        private MultiSelectList GetProjectFilter(List<int> selectedProject)
        {
            var all = ProjectManager.GetActive();

            var viewableProjects = ProjectManager.GetAppViewableProjects(this);

            all.RemoveAll(p => !viewableProjects.Any(s => s.Entity.Id == p.Entity.Id));

            Project allProjects = new Project() { Id = Constants.AllProjectsId, Name = GetResource(ResourceKeys.AllProjects) };

            var allProjectsList = all.Select(p => p.Entity).ToList();
            allProjectsList.Insert(0, allProjects);

            if (selectedProject.Count == 0)
            {
                if (allProjectsList.Count > 1)
                    selectedProject.Add(allProjectsList[1].Id);
                else
                    selectedProject.Add(allProjectsList[0].Id);
            }

            return new MultiSelectList(allProjectsList, "id", "Name", selectedProject);
        }

        [AppUrl("getprojectattributes")]
        public ActionResult GetProjectAttributes()
        {
            var projectIds = Request.Form["projectIds[]"] == "0" ? Request.Form["projectIds[]"].SplitEntries(',') : Request.Form["projectIds[]"].SplitEntries(',', 0);

            if (projectIds.Count == 1 && projectIds[0] != 0)
            {
                var projectAttributes = ProjectAttributeRepository.GetAll(projectIds[0]);
                var project = ProjectManager.Get(projectIds[0]);

                if (project != null && project.Entity.LeadId > 0)
                {
                    var user  = UserManager.Get(project.Entity.LeadId);
                    ProjectAttribute attribute = new ProjectAttribute() { Attributename = "Lead", Attributevalue = user.Fullname };
                    projectAttributes.Insert(0, attribute);
                }

                return JsonSuccess(new
                {
                    Html = RenderPartialViewToString(this, AppManager.Instance.GetAppUrl(AppGuid, "views/_ProjectAttributes.cshtml"), projectAttributes)
                });
            }
            else
            {
                return JsonSuccess(new
                {
                    Html = ""
                });
            }
        }

        [AppUrl("get")]
        public ActionResult Get(ReportOptions form)
        {
            if (form.ProjectIds.Contains(Constants.AllProjectsId))
            {
                var viewableProjects = ProjectManager.GetAppViewableProjects(this);

                form.ProjectIds = viewableProjects.Count > 0 ? viewableProjects.Select(p => p.Entity.Id).ToList() : new List<int>();
                form.AllProjectsSelected = true;                
            }
         
            form.SummaryChart = form.SummaryChart.GetValueOrDefault();

            return GetSummaryReport(form);
        }

        #region Project Summary Reports

        private ActionResult GetSummaryReport(ReportOptions options)
        {
            switch ((ReportTypes)options.Reports)
            {
                case ReportTypes.Summary:
                    return GetSummaryReportMaster(options);
                case ReportTypes.SummaryComponent:
                    return GetSummaryByComponent(options);
                case ReportTypes.SummaryVersion:
                    return GetSummaryByVersion(options);
                case ReportTypes.SummaryResource:
                    return GetSummaryByResource(options);
                case ReportTypes.SummaryType:
                    return GetSummaryByType(options);
                case ReportTypes.SummaryPriority:
                    return GetSummaryByPriority(options);
                case ReportTypes.SummarySeverity:
                    return GetSummaryBySeverity(options);
                case ReportTypes.SummaryStatus:
                    return GetSummaryByStatus(options);
                case ReportTypes.SummaryResolution:
                    return GetSummaryByResolution(options);
                default:
                    return null;
            }
        }

        private ActionResult GetSummaryReportMaster(ReportOptions options)
        {
            string projectNames = string.Empty;

            if (options.AllProjectsSelected)
            {
                projectNames = "All Projects";
            }
            else
            {
                var list = new List<ProjectDto>(options.ProjectIds.Count());
                list.AddRange(options.ProjectIds.Select(id => ProjectManager.Get(id)));
                projectNames = string.Join(", ", list.Select(s => s.Entity.Name).ToList());
            }

            return Json(JsonResponse(options, RenderPartialViewToString(this, AppManager.Instance.GetAppUrl(AppGuid, "views/_Summary.cshtml"), projectNames)));
        }

        private ActionResult GetSummaryByResolution(ReportOptions options)
        {
            var reportManager = new ReportManager(GeminiApp.Cache(), UserContext, GeminiContext);         
          
            var list = new List<ProjectDto>(options.ProjectIds.Count());
            list.AddRange(options.ProjectIds.Select(id => ProjectManager.Get(id)));
            var SummaryByComponent = reportManager.GetSummaryByResolution(list);
            foreach (var Summary in SummaryByComponent)
            {
                Summary.FilterName = "resolutions";
            }
            var resultModel = new ReportResultModel { UserContext = UserContext, Results = SummaryByComponent, Title = GetResource(ResourceKeys.Resolution), Flag = options.SummaryChart.GetValueOrDefault(), ProjectIds = options.ProjectIds, IncludeClosed = true };
            return Json(JsonResponse(options, RenderPartialViewToString(this, AppManager.Instance.GetAppUrl(AppGuid, "views/_SummaryItem.cshtml"), resultModel)));
        }

        private ActionResult GetSummaryByStatus(ReportOptions options)
        {
            var reportManager = new ReportManager(GeminiApp.Cache(), UserContext, GeminiContext);
        
            var list = new List<ProjectDto>(options.ProjectIds.Count());
            list.AddRange(options.ProjectIds.Select(id => ProjectManager.Get(id)));
            var SummaryByStatus = reportManager.GetSummaryByStatus(list);
            foreach (var Summary in SummaryByStatus)
            {
                Summary.FilterName = "statuses";
            }

            var resultModel = new ReportResultModel { UserContext = UserContext, Results = SummaryByStatus, Title = GetResource(ResourceKeys.Status), Flag = options.SummaryChart.GetValueOrDefault(), ProjectIds = options.ProjectIds };
            return Json(JsonResponse(options, RenderPartialViewToString(this, AppManager.Instance.GetAppUrl(AppGuid, "views/_SummaryItem.cshtml"), resultModel)));
        }

        private ActionResult GetSummaryBySeverity(ReportOptions options)
        {
            var reportManager = new ReportManager(GeminiApp.Cache(), UserContext, GeminiContext);
        
            var list = new List<ProjectDto>(options.ProjectIds.Count());
            list.AddRange(options.ProjectIds.Select(id => ProjectManager.Get(id)));
            var SummaryByComponent = reportManager.GetSummaryBySeverity(list);
            foreach (var Summary in SummaryByComponent)
            {
                Summary.FilterName = "severities";
            }
            var resultModel = new ReportResultModel { UserContext = UserContext, Results = SummaryByComponent, Title = GetResource(ResourceKeys.Severity), Flag = options.SummaryChart.GetValueOrDefault(), ProjectIds = options.ProjectIds };
            return Json(JsonResponse(options, RenderPartialViewToString(this, AppManager.Instance.GetAppUrl(AppGuid, "views/_SummaryItem.cshtml"), resultModel)));
        }

        private ActionResult GetSummaryByPriority(ReportOptions options)
        {
            var reportManager = new ReportManager(GeminiApp.Cache(), UserContext, GeminiContext);
          
            var list = new List<ProjectDto>(options.ProjectIds.Count());
            list.AddRange(options.ProjectIds.Select(id => ProjectManager.Get(id)));
            var SummaryByComponent = reportManager.GetSummaryByPriority(list);
            foreach (var Summary in SummaryByComponent)
            {
                Summary.FilterName = "priorities";
            }
            var resultModel = new ReportResultModel { UserContext = UserContext, Results = SummaryByComponent, Title = GetResource(ResourceKeys.Priority), Flag = options.SummaryChart.GetValueOrDefault(), ProjectIds = options.ProjectIds };
            return Json(JsonResponse(options, RenderPartialViewToString(this, AppManager.Instance.GetAppUrl(AppGuid, "views/_SummaryItem.cshtml"), resultModel)));
        }

        private ActionResult GetSummaryByType(ReportOptions options)
        {
            var reportManager = new ReportManager(GeminiApp.Cache(), UserContext, GeminiContext);
          
            var list = new List<ProjectDto>(options.ProjectIds.Count());
            list.AddRange(options.ProjectIds.Select(id => ProjectManager.Get(id)));
            var SummaryByType = reportManager.GetSummaryByType(list);
            foreach (var Summary in SummaryByType)
            {
                Summary.FilterName = "types";
            }
            var resultModel = new ReportResultModel { UserContext = UserContext, Results = SummaryByType, Title = GetResource(ResourceKeys.Type), Flag = options.SummaryChart.GetValueOrDefault(), ProjectIds = options.ProjectIds };
            return Json(JsonResponse(options, RenderPartialViewToString(this, AppManager.Instance.GetAppUrl(AppGuid, "views/_SummaryItem.cshtml"), resultModel)));
        }

        private ActionResult GetSummaryByResource(ReportOptions options)
        {
            var reportManager = new ReportManager(GeminiApp.Cache(), UserContext, GeminiContext);
 
            var list = new List<ProjectDto>(options.ProjectIds.Count());
            list.AddRange(options.ProjectIds.Select(id => ProjectManager.Get(id)));
            var SummaryByComponent = reportManager.GetSummaryByResource(list);
            foreach (var Summary in SummaryByComponent)
            {
                Summary.FilterName = "resources";
            }
            var resultModel = new ReportResultModel { UserContext = UserContext, Results = SummaryByComponent, Title = GetResource(ResourceKeys.Resources), Flag = options.SummaryChart.GetValueOrDefault(), ProjectIds = options.ProjectIds };
            return Json(JsonResponse(options, RenderPartialViewToString(this, AppManager.Instance.GetAppUrl(AppGuid, "views/_SummaryItem.cshtml"), resultModel)));
        }

        private ActionResult GetSummaryByComponent(ReportOptions options)
        {
            var reportManager = new ReportManager(GeminiApp.Cache(), UserContext, GeminiContext);

            var list = new List<ProjectDto>(options.ProjectIds.Count());
            list.AddRange(options.ProjectIds.Select(id => ProjectManager.Get(id)));
            var SummaryByComponent = reportManager.GetSummaryByComponent(list);
            foreach (var Summary in SummaryByComponent)
            {
                Summary.FilterName = "components";
            }
            var resultModel = new ReportResultModel { UserContext = UserContext, Results = SummaryByComponent, Title = GetResource(ResourceKeys.Components), Flag = options.SummaryChart.GetValueOrDefault(), ProjectIds = options.ProjectIds };
            return Json(JsonResponse(options, RenderPartialViewToString(this, AppManager.Instance.GetAppUrl(AppGuid, "views/_SummaryItem.cshtml"), resultModel)));
        }

        private ActionResult GetSummaryByVersion(ReportOptions options)
        {
            var reportManager = new ReportManager(GeminiApp.Cache(), UserContext, GeminiContext);
     
            var list = new List<ProjectDto>(options.ProjectIds.Count());
            list.AddRange(options.ProjectIds.Select(id => ProjectManager.Get(id)));
            var SummaryByVersion = reportManager.GetSummaryByVersion(list);
            foreach (var Summary in SummaryByVersion)
            {
                Summary.FilterName = "versions";
            }
            var resultModel = new ReportResultModel { UserContext = UserContext, Results = SummaryByVersion, Title = GetResource(ResourceKeys.Versions), Flag = options.SummaryChart.GetValueOrDefault(), ProjectIds = options.ProjectIds }; //TODO add new key for this
            return Json(JsonResponse(options, RenderPartialViewToString(this, AppManager.Instance.GetAppUrl(AppGuid, "views/_SummaryItem.cshtml"), resultModel)));
        }

        #endregion

        private JsonResponse JsonResponse(ReportOptions options, string html)
        {
            CurrentCard.Options[AppGuid] = options.ToJson();

            if (!CurrentCard.Url.HasValue())
            {
                CurrentCard.Url = NavigationHelper.GetReportsPageUrl(CurrentProject);
            }

            var r = new JsonResponse()
            {
                Success = true,
                Result = new { Html = html, SavedCard = CurrentCard }
            };
            return r;
        }
    }
}
