using System.ComponentModel.DataAnnotations;

namespace OutboxNet.SampleApp.Data
{
    public class SampleEntity
    {
        [Key]
        public Guid Id { get; set; }

        [StringLength(125)]
        public string? Data1 { get; set; }
    }
}
