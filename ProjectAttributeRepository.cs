using Countersoft.Gemini.Extensibility;
using ProjectSummary.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ProjectSummary.Data
{
    public class ProjectAttributeRepository
    {
        public static List<ProjectAttribute> GetAll(int projectId)
        {
            if (projectId == 0) return null;

            var query = string.Format(@"select a.*,gemini_projects.projectname                          
                          from gemini_projectattributes a
                          JOIN gemini_projects ON gemini_projects.projectid = a.projectid
                          where a.projectid = {0} order by gemini_projects.projectname asc, a.attributeorder asc", projectId);
            

            var result = SQLService.Instance.RunQuery<ProjectAttribute>(query).ToList();
            
            return result;
        }

    }
}
