using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using Countersoft.Gemini.Commons;
using Countersoft.Gemini.Commons.Dto;

namespace Summary
{
    public class SummaryModel
    {
        public ProjectDto CurrentProject { get; set; }
        public ReportOptions Options { get; set; }
        public IEnumerable<SelectListItem> ProjectList { get; set; }
    }

    public class SummaryCardModel
    {
        public List<int> ProjectIds { get; set; }
    }

    public class ReportOptions
    {
        public ReportOptions()
        {
            AllProjectsSelected = false;
        }

        public List<int> ProjectIds { get; set; }
        public int Reports { get; set; }
        public bool? SummaryChart { get; set; }
        public bool AllProjectsSelected { get; set; }
    }

    public enum ReportTypes
    {
        Summary = 30,
        SummaryComponent = 31,
        SummaryVersion = 32,
        SummaryResource = 33,
        SummaryType = 34,
        SummaryPriority = 35,
        SummarySeverity = 36,
        SummaryStatus = 37,
        SummaryResolution = 38
    }

    public class ReportResultModel
    {
        public ReportResultModel()
        {
            ProjectIds = new int[0];
        }

        public object Results { get; set; }
        public IEnumerable<int> ProjectIds { get; set; }
        public bool Flag { get; set; }

        public string FilterName { get; set; }
        public bool IncludeClosed { get; set; }
        public string FilterByClosedStatuses { get; set; }
                
        public string Title { get; set; }

        public Dictionary<int, string> Dictionary { get; set; }

        public IEnumerable<GraphResultItemModel> ResultsAsGraphItems
        {
            get { return (Results as IEnumerable<GraphResultItemModel>)/*.OrderByDescending(i => i.GraphCount)*/; }
        }

        public UserContext UserContext { get; set; }
    }

    public class GraphResultItemModel
    {
        public int GraphKeyId { get; set; }
        public string GraphKeyName { get; set; }

        public int GraphRefId { get; set; }
        public string GraphRefName { get; set; }

        public int GraphCount { get; set; }
        public double? GraphPercent { get; set; }

        public string FilterName { get; set; }
        public List<int> GraphKeys { get; set; }

        public GraphResultItemModel()
        {
            GraphKeys = new List<int>();
        }
    }
}
