using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace ExtensionMethodProjection
{
	class Program
	{
		static void Main(string[] args)
		{
			using (var ctx = new MyContext())
			{
				var b = ctx.People.AsExtendable().ToViewModels().ToArray();
			}
		}
	}

	public class MyContext : DbContext
	{
		public MyContext() : base("ExtensionMethodProjection") { }
		public DbSet<Person> People { get; set; }
		public DbSet<Profile> Profile { get; set; }

		protected override void OnModelCreating(DbModelBuilder modelBuilder)
		{
			modelBuilder.Entity<Person>().HasMany(x => x.Friends).WithMany();
			modelBuilder.Entity<Profile>().HasMany(x => x.SomethingElses).WithMany();
			modelBuilder.Entity<Person>().HasRequired(x => x.Me).WithMany().HasForeignKey(x => x.ProfileId).WillCascadeOnDelete(false);
			base.OnModelCreating(modelBuilder);
		}
	}

	public class Person
	{
		public int Id { get; set; }
		public int ProfileId { get; set; }
		public Profile Me { get; set; }
		public ICollection<Profile> Friends { get; set; }
	}
	public class Profile
	{
		public int Id { get; set; }
		public string Name { get; set; }
		public ICollection<SomethingElse> SomethingElses { get; set; }
	}

	public class SomethingElse
	{
		public int Id { get; set; }
		public string Name { get; set; }
	}

	public class PersonModel
	{
		public ProfileModel Me { get; set; }
		public IEnumerable<ProfileModel> Friends { get; set; }
	}
	public class ProfileModel
	{
		public string Name { get; set; }
		public IEnumerable<SomethingElseModel> SomethingElses { get; set; }
	}
	public class SomethingElseModel
	{
		public string Name { get; set; }
	}
	public static class Extensions
	{
		[ExpandableMethod]
		public static IQueryable<PersonModel> ToViewModels(this IQueryable<Person> entities)
		{
			return entities.Select(x => new PersonModel
			{
				Me = x.Me.ToViewModel(), //this method cannot be translated into a store expression
				Friends = x.Friends.AsQueryable().ToViewModels() //works fine with some magic (tm)
			});
		}

		[ExpandableMethod]
		public static IQueryable<ProfileModel> ToViewModels(this IQueryable<Profile> entities)
		{
			return entities.Select(x => new ProfileModel { Name = x.Name });
		}

		[ExpandableMethod]
		public static IQueryable<SomethingElseModel> ToViewModels(this IQueryable<SomethingElse> entities)
		{
			return entities.Select(x => new SomethingElseModel { Name = x.Name });
		}

		public static Expression<Func<Profile, ProfileModel>> ToPublicViewModelExpression()
		{
			return entity => new ProfileModel
			{
				Name = entity.Name,
				SomethingElses = entity.SomethingElses.AsQueryable().ToViewModels() //this depth breaks the parameter replacement
			};
		}
		[ReplaceInExpressionTree(MethodName = nameof(ToPublicViewModelExpression))]
		public static ProfileModel ToViewModel(this Profile entity)
		{
			return ToPublicViewModelExpression().Compile()(entity);
		}
	}
}
