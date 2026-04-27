using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Dragonfire.Outbox.SampleApp.Data
{
    public class SampleAppContext : DbContext
    {
        public SampleAppContext (DbContextOptions<SampleAppContext> options)
            : base(options)
        {
        }

        public DbSet<SampleEntity> SampleEntity { get; set; }
    }
}
