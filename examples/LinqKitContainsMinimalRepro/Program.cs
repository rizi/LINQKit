using System.Linq;
using LinqKit;

namespace LinqKitContainsMinimalRepro
{
    class Program
    {
        static void Main(string[] args)
        {
            using (var db = new MyContext())
            {
                db.Database.CreateIfNotExists();

                if (!db.MyModels.Any())
                {
                    db.MyModels.Add(new MyModel { Id = 1 });
                    db.MyModels.Add(new MyModel { Id = 2 });
                    db.MyModels.Add(new MyModel { Id = 3 });
                    db.MyModels.Add(new MyModel { Id = 4 });
                    db.MyModels.Add(new MyModel { Id = 5 });

                    db.SaveChanges();
                }
            }

            using (var db = new MyContext())
            {
                //var noExpandableIds = db.MyModels.Where(m => m.Id < 4).Select(m => m.Id);
                //var result1 = db.MyModels.Where(m => noExpandableIds.Contains(m.Id)).ToList(); // Triggers a single db hit

                //var noExpandableEntities = db.MyModels.Where(m => m.Id < 4);
                //var result2 = db.MyModels.Where(m => noExpandableEntities.Contains(m)).ToList(); // Triggers another single hit w/ the same SQL

                var expandableIds = db.MyModels.AsExpandable().Where(m => m.Id < 4).Select(m => m.Id);
                var result3 = db.MyModels.Where(m => expandableIds.Contains(m.Id)).ToList(); // Triggers two db hits
                // var result4 = db.MyModels.AsExpandable().Where(m => expandableIds.Contains(m.Id)).ToList(); // Triggers the same two hits

                // var expandableEntities = db.MyModels.AsExpandable().Where(m => m.Id < 4);
                // Can't do either of these w/ expandable queries.
                // Causes a System.NotSupportedException: Unable to create a constant value of type 'LinqKitContainsMinimalRepro.MyModel'. Only primitive types or enumeration types are supported in this context.
                //var result5 = db.MyModels.Where(m => expandableEntities.Contains(m)).ToList();
                //var result6 = db.MyModels.AsExpandable().Where(m => expandableEntities.Contains(m)).ToList();
            }
        }
    }
}
