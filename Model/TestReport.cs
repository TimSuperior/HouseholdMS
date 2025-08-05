using System;
using System.Collections.Generic;

namespace HouseholdMS.Model
{
    public class TestReport
    {
        public int ReportID { get; set; }
        public int HouseholdID { get; set; }
        public int TechnicianID { get; set; }
        public DateTime TestDate { get; set; }
        public string DeviceStatus { get; set; }
        public List<InspectionItem> InspectionItems { get; set; } = new List<InspectionItem>();
        public List<string> Annotations { get; set; } = new List<string>();
        public List<string> ImagePaths { get; set; } = new List<string>();
        public List<SettingsVerificationItem> SettingsVerification { get; set; } = new List<SettingsVerificationItem>();
    }

    public class InspectionItem
    {
        public string Name { get; set; }
        public string Result { get; set; }
        public string Annotation { get; set; }
    }

    public class SettingsVerificationItem
    {
        public string Parameter { get; set; }
        public string Value { get; set; }
        public string Status { get; set; }
    }
}
