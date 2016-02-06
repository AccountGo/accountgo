using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain
{
    [Table("Log")]
    public class Log : BaseEntity
    {
        public DateTime TimeStamp { get; set; }
        public string Level { get; set; }
        public string Logger { get; set; }
        public string Message { get; set; }
        public string Username { get; set; }
        public string CallSite { get; set; }
        public string Thread { get; set; }
        public string Exception { get; set; }
        public string StackTrace { get; set; }
    }
}
