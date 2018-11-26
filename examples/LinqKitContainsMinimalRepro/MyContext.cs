using System.Data.Entity;

namespace LinqKitContainsMinimalRepro
{
    public class MyContext : DbContext
    {
        public MyContext() : base("DefaultConnection") { }

        public DbSet<MyModel> MyModels { get; set; }
    }
}
