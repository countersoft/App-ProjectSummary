using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Text;
using Countersoft.Foundation.Commons.Enums;
using Countersoft.Foundation.Commons.Extensions;
using Countersoft.Foundation.Data;
using Countersoft.Gemini.Commons;
using Countersoft.Gemini.Commons.Dto;
using Countersoft.Gemini.Commons.Entity;
using Countersoft.Gemini.Commons.Permissions;
using Countersoft.Gemini.Contracts;
using Countersoft.Gemini.Contracts.Caching;
using Countersoft.Gemini.Data.Issues;
using Countersoft.Gemini.Models.Reports;
using NHibernate;
using NHibernate.Transform;
using System.Web.Mvc;
using Countersoft.Gemini.Commons.Meta;
using Countersoft.Gemini.Infrastructure.Managers;

namespace Summary
{
    public class ReportManager : BaseManager
    {
        public ReportManager(ICacheContainer cache, UserContext userContext, GeminiContext geminiContext)
            : base(cache, userContext, geminiContext)
        {
        }

        private string GetEveryoneGroups()
        {
            if (UserContext.User.Entity.Id == Constants.AnonymousUserId) return "1";

            return "1,3";
        }


        public static List<DayOfWeek> GetWeekends()
        {
            return new List<DayOfWeek> { DayOfWeek.Saturday, DayOfWeek.Sunday }; ;
        }

     
        #region Project Summary

        private IList<GraphResultItemModel> GetGraph(string sql, IEnumerable<ProjectDto> projects)
        {
            ISession session = new SessionManager().GetSession();

            var multi = session.CreateMultiQuery();

            /*new stuff*/
            IList<ProjectDto> viewOwn = new List<ProjectDto>();

            foreach (var project in projects)
            {
                if (UserContext.PermissionsManager.IsInRole(project.Entity, Roles.CanOnlyViewOwnItems))
                {
                    viewOwn.Add(project);
                }
            }

            var query = session.CreateSQLQuery(sql);
            query.SetInt32("userid", UserContext.User.Entity.Id);
            query.SetResultTransformer(Transformers.AliasToBean<GraphResultItemModel>());
            query.SetResultSetMapping("SummaryReportGraphResult");
            if (viewOwn.Any())
                query.SetParameterList("viewownproject", viewOwn.Select(p => p.Entity.Id).ToList());
            else
                query.SetParameterList("viewownproject", new List<int> { { 0 }, { 0 } });
            query.SetParameterList("projectid", projects.Select(p => p.Entity.Id).ToList());
            //multi.Add(query);

            /*end new*/


            /* foreach (var project in projects)
             {
                 var viewOwnOnly = UserContext.PermissionsManager.IsInRole(project.Entity, Roles.CanOnlyViewOwnItems);
                 //var query = session.CreateSQLQuery(sql);
                 query.SetBoolean("viewown", viewOwnOnly);
                 query.SetInt32("userid", UserContext.User.Entity.Id);
                 query.SetInt32("projectid", project.Entity.Id);
                 query.SetResultTransformer(Transformers.AliasToBean<GraphResultItemModel>());
                 query.SetResultSetMapping("SummaryReportGraphResult");
                 multi.Add(query);
             }*/

            var results = query.List<GraphResultItemModel>();

            var graphResultItemModels = new Dictionary<string, GraphResultItemModel>();
            foreach (var itemModel in results)
            {
                if (graphResultItemModels.ContainsKey(itemModel.GraphKeyName))
                {
                    graphResultItemModels[itemModel.GraphKeyName].GraphCount += itemModel.GraphCount;
                    if (!graphResultItemModels[itemModel.GraphKeyName].GraphKeys.Contains(itemModel.GraphKeyId))
                    {
                        graphResultItemModels[itemModel.GraphKeyName].GraphKeys.Add(itemModel.GraphKeyId);
                    }
                }
                else
                {
                    graphResultItemModels.Add(itemModel.GraphKeyName, itemModel);
                    itemModel.GraphKeys.Add(itemModel.GraphKeyId);
                }
            }
            return graphResultItemModels.Values.ToList();
        }

        public IList<GraphResultItemModel> GetGraphResults(IEnumerable<GraphResultItemModel> results)
        {

            var graphResultItemModels = new Dictionary<string, GraphResultItemModel>();
            foreach (var itemModel in results)
            {
                if (graphResultItemModels.ContainsKey(itemModel.GraphKeyName))
                {
                    graphResultItemModels[itemModel.GraphKeyName].GraphCount += itemModel.GraphCount;
                    if (!graphResultItemModels[itemModel.GraphKeyName].GraphKeys.Contains(itemModel.GraphRefId))
                    {
                        graphResultItemModels[itemModel.GraphKeyName].GraphKeys.Add(itemModel.GraphRefId);
                    }
                }
                else
                {
                    graphResultItemModels.Add(itemModel.GraphKeyName, itemModel);
                    itemModel.GraphKeys.Add(itemModel.GraphRefId);
                }
            }
            return graphResultItemModels.Values.ToList();
        }

        public IList<GraphResultItemModel> GetSummaryByComponent(List<ProjectDto> projects)
        {
            var sql =
                @"SELECT a.componentid AS GraphKeyId,
                        a.componentname AS GraphKeyName,
                        a.parentcomponentid AS GraphRefId,
                        ISNULL(b.issuecount, 0) AS GraphCount
                    FROM gemini_components a 
 	                    LEFT OUTER JOIN 
		                    (SELECT componentid, COUNT(*) AS issuecount FROM gemini_issuecomponents ic WHERE ic.issueid IN 
			                ( 
				                SELECT issueid FROM gemini_issues 
                                WHERE projectid in (:projectid)
					                AND issuestatusid NOT IN (SELECT statusid FROM gemini_issuestatus WHERE isfinal=1)
					                AND (projectid NOT IN (:viewownproject) 
                                        OR (projectid IN (:viewownproject) 
                                            AND (gemini_issues.reportedby = :userid 
                                                OR :userid in (select ir.userid from gemini_issueresources ir where ir.issueid = gemini_issues.issueid) ) ) )
					                AND (gemini_issues.visibility IN ({0}) OR :userid in (select pg.userid from gemini_projectgroupmembership pg where pg.projectgroupid = gemini_issues.visibility))
			                )
			                GROUP BY componentid
		                ) b
	                    ON a.componentid=b.componentid
	                WHERE a.projectid in (:projectid)
	                ORDER BY a.componentorder, a.componentname";
            return GetGraph(string.Format(sql, GetEveryoneGroups()), projects);
        }

        public IList<GraphResultItemModel> GetSummaryByVersion(List<ProjectDto> projects)
        {
            var sql =
                @"SELECT 0 AS GraphKeyId, 31 AS GraphRefId, N'* Unscheduled *' AS GraphKeyName,COUNT(*) AS GraphCount,-1 AS versionorder, 0 AS pid
                    FROM gemini_issues  
		            WHERE projectid IN (:projectid)
		                AND issuestatusid NOT IN (SELECT statusid FROM gemini_issuestatus WHERE isfinal=1)  
		                AND (fixedinversionid=0 or fixedinversionid is null)  
		                        AND (projectid NOT IN (:viewownproject) 
                                        OR (projectid IN (:viewownproject) 
                                            AND (gemini_issues.reportedby = :userid 
                                                OR :userid in (select ir.userid from gemini_issueresources ir where ir.issueid = gemini_issues.issueid) ) ) )

		                AND (gemini_issues.visibility IN ({0}) OR :userid in (select pg.userid from gemini_projectgroupmembership pg where pg.projectgroupid = gemini_issues.visibility))
		            GROUP BY fixedinversionid  
	        UNION ALL  
	            SELECT a.versionid AS GraphKeyId, 31 AS GraphRefId, a.versionname AS GraphKeyName, ISNULL(b.issuecount, 0) AS GraphCount,a.versionorder, a.projectid AS pid
                FROM gemini_versions a  
		            LEFT OUTER JOIN  
		                (SELECT fixedinversionid, COUNT(*) AS issuecount FROM gemini_issues 
                            WHERE projectid IN (:projectid)
		                        AND issuestatusid NOT IN (SELECT statusid FROM gemini_issuestatus WHERE isfinal = 1)  
		                        AND (projectid NOT IN (:viewownproject) 
                                        OR (projectid IN (:viewownproject) 
                                            AND (gemini_issues.reportedby = :userid 
                                                OR :userid in (select ir.userid from gemini_issueresources ir where ir.issueid = gemini_issues.issueid) ) ) )
		                        AND (gemini_issues.visibility IN ({0}) OR :userid in (select pg.userid from gemini_projectgroupmembership pg where pg.projectgroupid = gemini_issues.visibility))  
		                    GROUP BY fixedinversionid) AS b   
		            ON a.versionid=b.fixedinversionid  
		        WHERE projectid  IN (:projectid) AND a.versionreleased=0 AND a.versionarchived=0  
	            ORDER BY pid , versionorder  ";
            return GetGraph(string.Format(sql, GetEveryoneGroups()), projects);
        }

        public IList<GraphResultItemModel> GetSummaryByResource(List<ProjectDto> projects)
        {
            var sql =
                @"SELECT 0 AS GraphKeyId, 33 AS GraphRefId, COUNT(*) AS GraphCount, N'* Unassigned *' AS GraphKeyName
                    FROM gemini_issues 
				    WHERE projectid IN (:projectid) AND issuestatusid NOT IN (SELECT statusid FROM gemini_issuestatus WHERE isfinal=1)
				        AND NOT EXISTS (SELECT * FROM gemini_issueresources WHERE gemini_issueresources.issueid = gemini_issues.issueid)
		                        AND (projectid NOT IN (:viewownproject) 
                                        OR (projectid IN (:viewownproject) 
                                            AND (gemini_issues.reportedby = :userid 
                                                OR :userid in (select ir.userid from gemini_issueresources ir where ir.issueid = gemini_issues.issueid) ) ) )

				        AND (gemini_issues.visibility IN ({0}) OR :userid IN (select pg.userid FROM gemini_projectgroupmembership pg WHERE pg.projectgroupid = gemini_issues.visibility))
			    	GROUP BY issueid-issueid
		        UNION ALL
		            SELECT u.userid AS GraphKeyId, 33 AS GraphRefId, COUNT(gi.issueid) AS GrahCount, firstname + ' ' +surname AS GraphKeyName 
			            FROM gemini_issues gi
			                INNER JOIN gemini_issueresources gir
                                ON gir.issueid = gi.issueid
                            INNER JOIN gemini_users u
                                ON gir.userid = u.userid
				        WHERE projectid IN (:projectid)
						    AND gi.issuestatusid NOT IN (SELECT statusid FROM gemini_issuestatus WHERE isfinal=1)
		                        AND (projectid NOT IN (:viewownproject) 
                                        OR (projectid IN (:viewownproject) 
                                            AND (gi.reportedby = :userid 
                                                OR :userid in (select ir.userid from gemini_issueresources ir where ir.issueid = gi.issueid) ) ) )

						    AND (gi.visibility IN ({0}) OR :userid in (select pg.userid from gemini_projectgroupmembership pg where pg.projectgroupid = gi.visibility))
				         GROUP BY u.userid, u.firstname, u.surname ORDER BY GraphKeyName";
            return GetGraph(string.Format(sql, GetEveryoneGroups()), projects);
        }


        public string GetTemplateOrdering(List<ProjectDto> projects)
        {
            if (projects.Select(p => p.Entity.TemplateId).Distinct().Count() > 1)
            {
                return "ORDER BY GraphKeyName, a.Seq";
            }
            return "ORDER BY a.Seq";
        }

        public IList<GraphResultItemModel> GetSummaryByType(List<ProjectDto> projects)
        {
            var sql =
                @"SELECT a.typeid AS GraphKeyId, 34 AS GraphRefId, a.typedesc AS GraphKeyName, ISNULL(b.issuecount, 0) AS GraphCount
		            FROM gemini_issuetypes a 
	                    LEFT OUTER JOIN
	                        (SELECT issuetypeid, COUNT(*) AS issuecount FROM gemini_issues 
                            WHERE projectid IN (:projectid)
		                        AND issuestatusid NOT IN (SELECT statusid FROM gemini_issuestatus WHERE isfinal=1)
		                        AND (projectid NOT IN (:viewownproject) 
                                        OR (projectid IN (:viewownproject) 
                                            AND (gemini_issues.reportedby = :userid 
                                                OR :userid in (select ir.userid from gemini_issueresources ir where ir.issueid = gemini_issues.issueid) ) ) )

		                        AND (gemini_issues.visibility IN ({0}) OR :userid IN (SELECT pg.userid FROM gemini_projectgroupmembership pg WHERE pg.projectgroupid = gemini_issues.visibility))
		                    GROUP BY issuetypeid) b
		                ON a.typeid=b.issuetypeid
                    WHERE a.templateid IN (SELECT templateid FROM gemini_projects WHERE projectid IN (:projectid))";

            return GetGraph(string.Format(sql, GetEveryoneGroups()) + GetTemplateOrdering(projects), projects);
        }

        public IList<GraphResultItemModel> GetSummaryByPriority(List<ProjectDto> projects)
        {
            var sql =
                @"SELECT a.priorityid AS GraphKeyId, 35 AS GraphRefId, a.prioritydesc AS GraphKeyName, ISNULL(b.issuecount, 0) AS GraphCount
		            FROM gemini_issuepriorities a 
		                LEFT OUTER JOIN 
		                    (SELECT issuepriorityid, COUNT(*) AS issuecount FROM gemini_issues 
                            WHERE projectid IN (:projectid)
		                        AND issuestatusid NOT IN (SELECT statusid FROM gemini_issuestatus WHERE isfinal=1)
		                        AND (projectid NOT IN (:viewownproject) 
                                        OR (projectid IN (:viewownproject) 
                                            AND (gemini_issues.reportedby = :userid 
                                                OR :userid in (select ir.userid from gemini_issueresources ir where ir.issueid = gemini_issues.issueid) ) ) )

		                        AND (gemini_issues.visibility IN ({0}) OR :userid in (select pg.userid from gemini_projectgroupmembership pg where pg.projectgroupid = gemini_issues.visibility))
		                    GROUP BY issuepriorityid) b
		                ON a.priorityid=b.issuepriorityid
                    WHERE a.templateid IN (SELECT templateid FROM gemini_projects WHERE projectid IN (:projectid))";

            return GetGraph(string.Format(sql, GetEveryoneGroups()) + GetTemplateOrdering(projects), projects);
        }

        public IList<GraphResultItemModel> GetSummaryBySeverity(List<ProjectDto> projects)
        {
            var sql =
                @"SELECT a.severityid AS GraphKeyId, 36 AS GraphRefId, a.severitydesc AS GraphKeyName, ISNULL(b.issuecount, 0) AS GraphCount
		            FROM gemini_issueseverity a 
		                LEFT OUTER JOIN
		                    (SELECT issueseverityid, COUNT(*) AS issuecount FROM gemini_issues 
                            WHERE projectid IN (:projectid)
		                        AND issuestatusid NOT IN (SELECT statusid FROM gemini_issuestatus WHERE isfinal = 1)
		                        AND (projectid NOT IN (:viewownproject) 
                                        OR (projectid IN (:viewownproject) 
                                            AND (gemini_issues.reportedby = :userid 
                                                OR :userid in (select ir.userid from gemini_issueresources ir where ir.issueid = gemini_issues.issueid) ) ) )

		                        AND (gemini_issues.visibility IN ({0}) OR :userid in (select pg.userid from gemini_projectgroupmembership pg where pg.projectgroupid = gemini_issues.visibility))
		                    GROUP BY issueseverityid) b
		                ON a.severityid=b.issueseverityid
                    WHERE a.templateid IN (SELECT templateid FROM gemini_projects WHERE projectid IN (:projectid))";

            return GetGraph(string.Format(sql, GetEveryoneGroups()) + GetTemplateOrdering(projects), projects);
        }

        public IList<GraphResultItemModel> GetSummaryByStatus(List<ProjectDto> projects)
        {
            var sql =
                @"SELECT a.statusid AS GraphKeyId, 37 AS GraphRefId, a.statusdesc AS GraphKeyName,ISNULL(b.issuecount, 0) AS GraphCount
		            FROM gemini_issuestatus a 
			            LEFT OUTER JOIN 
		                    (SELECT issuestatusid, COUNT(*) AS issuecount 
                            FROM gemini_issues WHERE projectid IN (:projectid)
		                        AND (projectid NOT IN (:viewownproject) 
                                        OR (projectid IN (:viewownproject) 
                                            AND (gemini_issues.reportedby = :userid 
                                                OR :userid in (select ir.userid from gemini_issueresources ir where ir.issueid = gemini_issues.issueid) ) ) )
		                        AND (gemini_issues.visibility IN ({0}) OR :userid in (select pg.userid from gemini_projectgroupmembership pg where pg.projectgroupid = gemini_issues.visibility))
		                    GROUP BY issuestatusid) b
		                ON a.statusid=b.issuestatusid
                    WHERE a.templateid IN (SELECT templateid FROM gemini_projects WHERE projectid IN (:projectid))";

            // added "WHERE a.isfinal = 0" as others don't show closed items - but maybe this is useful, maybe not? 5k closed items, and 5 open won't show up very well....
            return GetGraph(string.Format(sql, GetEveryoneGroups()) + GetTemplateOrdering(projects), projects);
        }

        public IList<GraphResultItemModel> GetSummaryByResolution(List<ProjectDto> projects)
        {
            var sql =
                @"SELECT a.resolutionid AS GraphKeyId, 38 AS GraphRefId, a.resdesc AS GraphKeyName, ISNULL(b.issuecount, 0) AS GraphCount
		                FROM gemini_issueresolutions a 
		                    LEFT OUTER JOIN 
		                        (SELECT issueresolutionid, COUNT(*) AS issuecount 
                                FROM gemini_issues 
                                WHERE projectid IN (:projectid)
		                            AND (projectid NOT IN (:viewownproject) 
                                        OR (projectid IN (:viewownproject) 
                                            AND (gemini_issues.reportedby = :userid 
                                                OR :userid in (select ir.userid from gemini_issueresources ir where ir.issueid = gemini_issues.issueid) ) ) )
		                            AND (gemini_issues.visibility IN ({0}) OR :userid in (select pg.userid from gemini_projectgroupmembership pg where pg.projectgroupid = gemini_issues.visibility))
		                        GROUP BY issueresolutionid) b
		                    ON a.resolutionid=b.issueresolutionid
                        WHERE a.templateid IN (SELECT templateid FROM gemini_projects WHERE projectid IN (:projectid))";

            return GetGraph(string.Format(sql, GetEveryoneGroups()) + GetTemplateOrdering(projects), projects);
        }
        #endregion

      
             

        /*public IEnumerable<SelectListItem> GetTimeFilter(string[] selected, int[] projectIds)
        {
            var manager = new MetaManager(GeminiApp.Cache(), UserContext, GeminiContext);
            var projectManager = new ProjectManager(this);

            var projects = projectManager.Get(new List<int>(projectIds));
            var templateid = 0;
            List<TimeType> Timelist = new List<TimeType>();
            foreach (ProjectDto project in projects)
            {
                var template = GeminiContext.Projects.Get(project.Entity.Id);
                if (template != null)
                {
                    templateid = template.TemplateId;
                    List<TimeType> timeTypes = timeTypes = manager.TimeTypeGetAll(templateid);
                    //Timelist.AddRange(timeTypes);
                    foreach (var type in timeTypes)
                    {
                        var tag = string.Concat(type.Id, '|');
                        var current = Timelist.Find(t => string.Compare(t.Label, type.Label, StringComparison.InvariantCultureIgnoreCase) == 0);
                        if (current == null)
                        {
                            type.Tag = tag;
                            Timelist.Add(type);
                        }
                        else
                        {
                            if (!current.Tag.Contains(string.Concat('|', tag)))
                            {
                                current.Tag = string.Concat(current.Tag, tag);
                            }
                        }
                    }
                }
            }

            return new SelectList(Timelist, "Tag", "Label", selected);
        }*/

        /*public IEnumerable<SelectListItem> GetReourceFilter(int[] selectedResources, int[] projectIds)
        {
            var manager = new UserManager(GeminiApp.Cache(), UserContext, GeminiContext);

            List<User> projectResources = manager.GetProjectResources(projectIds.ToList()).Select(p => p.Entity).ToList();
            //projectResources.Insert(0, new User{Id = 0, Firstname = GetResource(ResourceKeys.Select)});
            var result = new Collection<SelectListItem>();
            //(projectResources, "id", "Fullname", selected);
            foreach (var projectResource in projectResources)
            {
                var item = new SelectListItem { Text = projectResource.Fullname, Value = projectResource.Id.ToString(CultureInfo.InvariantCulture) };
                if (selectedResources != null) item.Selected = selectedResources.Contains(projectResource.Id);
                result.Add(item);
            }

            return result;
        }*/


        /*private string GetInFromList<T>(List<T> list, Func<T, int> func)
        {
            if (list == null || list.Count == 0) return "0";

            StringBuilder sb = new StringBuilder(list.Count * 5);
            foreach (var i in list)
            {
                sb.Append(func.Invoke(i));
                sb.Append(',');
            }

            sb.Remove(sb.Length - 1, 1);

            return sb.ToString();
        }
        protected List<T> MakeList<T>(IList<T> list)
        {
            if (list is List<T>) return list as List<T>;

            return new List<T>(list);
        }*/


    }

    /*public static class DateTimeExtensions
    {
        public static int GetFirstDayOfWeekSettingForSQL(this DateTime dt, string languageCode)
        {
            DayOfWeek dow = dt.GetFirstDayOfWeekSetting(languageCode);
            if (dow == DayOfWeek.Sunday)
            {
                return 7;
            }

            return (int)dow;
        }

        public static int GetNumberOfWeekends(this DateTime d, DateTime toDate, List<DayOfWeek> weekends)
        {
            DateTime current = d.ClearTime();
            DateTime end = toDate.ClearTime();
            int totalWeekendDays = 0;

            while (current <= end)
            {
                if (weekends.Contains(current.DayOfWeek)) totalWeekendDays++;

                current = current.AddDays(1);
            }

            return totalWeekendDays;
        }
    }*/
}