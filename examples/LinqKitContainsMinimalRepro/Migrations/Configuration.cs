namespace LinqKitContainsMinimalRepro.Migrations
{
    using System;
    using System.Data.Entity;
    using System.Data.Entity.Migrations;
    using System.Linq;

    internal sealed class Configuration : DbMigrationsConfiguration<LinqKitContainsMinimalRepro.MyContext>
    {
        public Configuration()
        {
            AutomaticMigrationsEnabled = false;
        }

        protected override void Seed(LinqKitContainsMinimalRepro.MyContext context)
        {
            var count = context.MyModels.Count();

            for (; count < 25; count++)
            {
                context.MyModels.Add(new MyModel());
            }

            context.SaveChanges();
        }
    }
}
