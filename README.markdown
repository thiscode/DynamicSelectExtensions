These extensions solve some problems using IQueryable:

1. Build a dynamic select statement (projection), with only specific fields, defined at runtime
2. Include related entities in Entity Framework with specific conditions (filter, orders or limits) in eager loading
3. Include related entities in Entity Framework defined at runtime

Please look into the file "IQueryableExtensions.cs". Here you can find a detailed explanation
of "how it was done".

Please have a look into the file "THANKS". Here are listed all people and sources that helped
me building this assembly.

The Problem Nr 1:
=================

Imagine you have a query like this:

	var query = query.Select( x => new {

		field1 = x.field1,
		field2 = x.field2,
		[...]

	});

Now you want to make the projection into the anonymous type more
dynamic. For example the frontend gives the user the possibility
to select what fields to return. You would have to write for every
possible combination an own query.

The Solution:
=============

After adding a reference to this assembly and add an

	using thiscode.Tools.DynamicSelectExtensions

You can do this:

	var query = query.SelectPartially( new List<string>(

		"field1",
		"field2",
		[...]

	));

The List inside the Select-Method could you, of course, built
previously dynamic.

You will get back an IQueryable&lt;dynamic&gt; object. This is the reason
why intellisense will not show you all members of the retrieved
entities. But you can access nevertheless the members:

	dynamic FirstObj = query.FirstOrDefault();
	Console.WriteLine(FirstObj.field1);

The Problem Nr 2 and 3:
=======================

Using the "Include" method of EF allows you to include related entities.
But it is an all-or-nothing-option:

	var query = query.Include("NavigationProperty1");

What if you do not want to include all related entities within a
navigation property?

What if you want to order or otherwise manipulate the related entities?

In pure Sql you could solve this by defining an additional condition
within the ON-Clause. But in EF it is not possible.

The Solution:
=============

After adding a reference to this assembly and add an

	using thiscode.Tools.DynamicSelectExtensions

You can do this:

	var query = query.SelectIncluding( new List<Expression<Func<T,object>>>>(){

		//Example how to retrieve only the newest history entry
		x => x.HistoryEntries.OrderByDescending(x => x.Timestamp).Take(1),

		//Example how to order related entities
		x => x.OtherEntities.OrderBy(y => y.Something).ThenBy(y => y.SomeOtherThing),

		//Example how to retrieve entities one level deeper
		x => x.CollectionWithRelations.Select(x => x.EntityCollectionOnSecondLevel),

		//Of course you can order or subquery the deeper level
		//Here you should use SelectMany, to flatten the query
		x => x.CollectionWithRelations.SelectMany(x => x.EntityCollectionOnSecondLevel.OrderBy(y => y.Something).ThenBy(y => y.SomeOtherThing)),

	});

The List inside the Select-Method could you, of course, built
previously dynamic.

You have to pay attention, that this call will use an AsEnumerable() in your
query chain. This means, your database will be queried after you request
an enumerator on this query at this point of your chain.
