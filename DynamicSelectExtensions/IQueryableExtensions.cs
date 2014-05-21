// Copyright 2013 Thomas Miliopoulos
// This file is a part of DynamicSelectExtensions and is licensed under the MS-PL
// See LICENSE.txt for details or visit http://www.opensource.org/licenses/ms-pl.html
// https://github.com/thiscode/DynamicSelectExtensions
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace thiscode.Tools.DynamicSelectExtensions
{
    public static class IQueryableExtensions
    {
        /// <summary>
        /// Use this to select only specific fields, instead fetching the whole entity.
        /// </summary>
        /// <example><code>
        /// var query =
        ///     YOUR_QUERYABLE_OBJECT
        ///     .SelectDynamicFields(new List<string>(){
        ///         "Property1",
        ///         "Property2",
        ///     });
        ///
        /// dynamic FirstObj = query.FirstOrDefault();
        /// Console.WriteLine(FirstObj.Property1); //Name of the member will validated in runtime!
        /// </code></example>
        /// <typeparam name="T">Type of Source IQueryable</typeparam>
        /// <param name="source">Source IQueryable</param>
        /// <param name="propertyNames">List of Property-Names you want to Select</param>
        /// <returns>A dynamic IQueryable Object. The object includes all Property-Names you have given as Fields.</returns>
        public static IQueryable<dynamic> SelectPartially<T>(this IQueryable<T> source, IEnumerable<string> propertyNames)
        {
            if (source == null) throw new ArgumentNullException("Source Object is NULL");

            //Here we do something similar to
            //
            //  Select(source => new {
            //      property1 = source.property1,
            //      property2 = source.property2,
            //      [...]
            //  })
            //
            //We build here firstly the Expression needed by the Select-Method dynamicly.
            //Beyond this we build even the class dynamicly. The class includes only
            //the Properties we want to project. The difference is, that the class is
            //not an anonymous type. Its a "Type built in Runtime" using Reflection.Emit.

            //Prepare ParameterExpression refering to the source object
            var sourceItem = Expression.Parameter(source.ElementType, "t");

            //Get PropertyInfos from Source Object (Filter all Misspelled Property-Names)
            var sourceProperties = propertyNames.Where(name => source.ElementType.GetProperty(name) != null).ToDictionary(name => name, name => source.ElementType.GetProperty(name));

            //Build dynamic a Class that includes the Fields (no inheritance, no interfaces)
            var dynamicType = DynamicTypeBuilder.GetDynamicType(sourceProperties.Values.ToDictionary(f => f.Name, f => f.PropertyType), typeof(object), Type.EmptyTypes);

            //Create the Binding Expressions
            var bindings = dynamicType.GetProperties().Where(p => p.CanWrite)
                .Select(p => Expression.Bind(p, Expression.Property(sourceItem, sourceProperties[p.Name]))).OfType<MemberBinding>().ToList();

            //Create the Projection
            var selector = Expression.Lambda<Func<T, dynamic>>(Expression.MemberInit(Expression.New(dynamicType.GetConstructor(Type.EmptyTypes)), bindings), sourceItem);

            //Now Select and return the IQueryable object
            return source.Select(selector);
        }

        /// <summary>
        /// <para>
        /// Selecting and including related entities, instead of using the "Include"-Method from EF.
        /// With the "Include"-Method it is not possible to specify conditions what entities to include.
        /// With this method you can specify exactly what entities to load, and how they will be filtered
        /// or ordered.
        /// </para>
        /// <para>
        /// Attention! This selection will use AsEnumerable(). This means, that the database will be
        /// queried at this point of the chain!
        /// </para>
        /// </summary>
        /// <typeparam name="T">Type of Source IQueryable</typeparam>
        /// <param name="source">Source IQueryable</param>
        /// <param name="includeExpessions">Lamda Expressions, defining what entities to include</param>
        /// <returns></returns>
        public static IEnumerable<T> SelectIncluding<T>(this IQueryable<T> source, IEnumerable<Expression<Func<T, object>>> includeExpessions)
        {
            if (source == null) throw new ArgumentNullException("Source Object is NULL");


            //Here we do something similar to this:

            //First, select into a anonymous type with all the related entity-collections you need.
            //The relation can be defined by every query you want.

            //var query = _dbSet
            //    .Select(mainEntity => new
            //    {
            //
            //        //The main object. We need this field to unwrap later.
            //        mainEntity,
            //
            //        //Example how to retrieve only the newest history entry
            //        newestHistoryEntry = mainEntity.HistoryEntries.OrderByDescending(x => x.Timestamp).Take(1),
            //
            //        //Example how to order related entities
            //        itemSpecMSRPrices = mainEntity.OtherEntities.OrderBy(y => y.Something).ThenBy(y => y.SomeOtherThing),
            //
            //        //Example how to retrieve entities one level deeper
            //        secondLevel = mainEntity.CollectionWithRelations.Select(x => x.EntityCollectionOnSecondLevel),
            //
            //        //Of course you can order or subquery the deeper level
            //        //Here you should use SelectMany, to flatten the query
            //        secondLevelOrdered = mainEntity.CollectionWithRelations.SelectMany(x => x.EntityCollectionOnSecondLevel.OrderBy(y => y.Something).ThenBy(y => y.SomeOtherThing)),
            //
            //    });

            //Now we fire up the query (AsEnumerable) and then unwrap the SupplierItem out
            //of the anonymous type (Select).

            //return query.AsEnumerable().Select(mainEntity => mainEntity.mainEntity);

            //Because the ObjectContext have collected all the related entities, it have also linked each other correctly over
            //navigation-properties and reference-properties. Please read this Tip, too:
            //http://blogs.msdn.com/b/alexj/archive/2009/10/13/tip-37-how-to-do-a-conditional-include.aspx

            //We build here firstly the Expression needed by the Select-Method dynamicly.
            //Beyond this we build even the class dynamicly. The class includes only
            //the Properties we want to project. The difference is, that the class is
            //not an anonymous type. Its a "Type built in Runtime" using Reflection.Emit.

            //Remark to the fields within the dynamic generated class:

            //All of them will be declared with the type really needed. So even the dynamicly
            //generated class will be "strong-type-safe".

            //Remark to the paramter "includeExpressions":

            //The method expect a collection of LambdaExpression-Objects. In fact we do not need
            //the LamdaExpression, but only the body of them. The LambdaExpression is only a
            //pleasant way to enable the user to define expression in a strong-type way.

            //Prepare ParameterExpression refering to the source object
            var sourceItem = Expression.Parameter(source.ElementType, "t");

            //Prepare helper class to replace the user-parameter of the LambdExpression with ours
            var paramRewriter = new PredicateRewriterVisitor(sourceItem);

            //Loop all expression and:
            //  1.) Determine returned type.
            //  2.) Get Body and replace the Parameter used by the user with ours
            //  2.) Give all of them a name and save them in a Dictionary
            Dictionary<string, Tuple<Expression, Type>> dynamicFields = new Dictionary<string, Tuple<Expression, Type>>();
            int dynamicFieldsCounter = 0;
            foreach (Expression<Func<T, object>> includeExpession in includeExpessions)
            {
                //Detect Type
                Type typeDetected;
                if ((includeExpession.Body.NodeType == ExpressionType.Convert) ||
                    (includeExpession.Body.NodeType == ExpressionType.ConvertChecked))
                {
                    var unary = includeExpession.Body as UnaryExpression;
                    if (unary != null)
                        typeDetected = unary.Operand.Type;
                }
                typeDetected = includeExpession.Body.Type;
                //Save into List
                dynamicFields.Add("f" + dynamicFieldsCounter, new Tuple<Expression, Type>(
                    paramRewriter.ReplaceParameter(includeExpession.Body, includeExpession.Parameters[0]),
                    typeDetected
                    ));
                //Count
                dynamicFieldsCounter++;
            }

            //Add a field in which the source object will be saved
            dynamicFields.Add("sourceObject", new Tuple<Expression, Type>(
                sourceItem,
                source.ElementType
                ));

            //Build dynamic a Class that includes the Fields (no inheritance, no interfaces)
            var dynamicType = DynamicTypeBuilder.GetDynamicType(dynamicFields.ToDictionary(x => x.Key, x => x.Value.Item2), typeof(object), Type.EmptyTypes);

            //Create the Binding Expressions
            var bindings = dynamicType.GetFields().Select(p => Expression.Bind(p, dynamicFields[p.Name].Item1)).OfType<MemberBinding>().ToList();

            //Create the Projection
            var selector = Expression.Lambda<Func<T, dynamic>>(Expression.MemberInit(Expression.New(dynamicType.GetConstructor(Type.EmptyTypes)), bindings), sourceItem);

            return source.Select(selector).AsEnumerable().Select(x => (T)x.sourceObject);
        }


        /// <summary>
        /// Helper class
        /// </summary>
        private class PredicateRewriterVisitor : ExpressionVisitor
        {
            private ParameterExpression _parameterExpression;
            private ParameterExpression _parameterExpressionToReplace;
            public PredicateRewriterVisitor(ParameterExpression parameterExpression)
            {
                _parameterExpression = parameterExpression;
            }
            public Expression ReplaceParameter(Expression node, ParameterExpression parameterExpressionToReplace)
            {
                _parameterExpressionToReplace = parameterExpressionToReplace;
                return base.Visit(node);
            }
            protected override Expression VisitParameter(ParameterExpression node)
            {
                if (node == _parameterExpressionToReplace) return _parameterExpression;
                else return node;
            }
        }
    }
}

                              