using System;
using System.Linq;
using System.Reflection;
using Sitecore.Configuration;
using Sitecore.ContentSearch;
using Sitecore.ContentSearch.Abstractions;
using Sitecore.ContentSearch.Diagnostics;
using Sitecore.ContentSearch.Linq;
using Sitecore.ContentSearch.Linq.Common;
using Sitecore.ContentSearch.Linq.Parsing;
using Sitecore.ContentSearch.Linq.Solr;
using Sitecore.ContentSearch.Pipelines.QueryGlobalFilters;
using Sitecore.ContentSearch.SolrProvider;
using Sitecore.Diagnostics;

namespace Sitecore.HighlightDemo.Solr
{
    public static class SolrHighlightExtension
    {
        public static IQueryable<TItem> GetExtendedQueryable<TItem>(this IProviderSearchContext context, params IExecutionContext[] executionContexts)
        {
            Assert.ArgumentNotNull(context, "context");
            var solrContext = context as SolrSearchContext;
            Assert.IsNotNull(solrContext, "context is not SolrSearchContext");

            var index = new LinqToSolrIndexExtended<TItem>(solrContext, executionContexts);
            if (Settings.GetBoolSetting("ContentSearch.EnableSearchDebug", false))
            {
                (index as IHasTraceWriter).TraceWriter = new LoggingTraceWriter(SearchLog.Log);
            }
            QueryGlobalFiltersArgs args = new QueryGlobalFiltersArgs(index.GetQueryable(), typeof(TItem), executionContexts.ToList<IExecutionContext>());
            solrContext.Index.Locator.GetInstance<Sitecore.Abstractions.ICorePipeline>().Run("contentSearch.getGlobalLinqFilters", args);
            return (IQueryable<TItem>)args.Query;
        }

        public static SearchResultsWithHighlights<T> GetResultsWithHighlights<T>(this IQueryable<T> source, string[] fieldNames, int surroundingcharacters = 20, int maxnumberofaccurencies = 5, string htmltag = "em")
        {
            Assert.ArgumentNotNull(source, "source");

            var queryable = source as GenericQueryable<T, SolrCompositeQuery>;
            Assert.IsNotNull(queryable, queryable.GetType());

            var expression = source.Expression;

            // Build a native query 
            var intermidQuery = (SolrCompositeQuery)queryable.GetType().InvokeMember("GetQuery",
                BindingFlags.InvokeMethod | BindingFlags.Instance | BindingFlags.NonPublic,
                Type.DefaultBinder, queryable, new object[] { expression });

            var linqIndex = (LinqToSolrIndexExtended<T>)queryable.GetType().InvokeMember("Index",
               BindingFlags.GetProperty | BindingFlags.Instance | BindingFlags.NonPublic,
               Type.DefaultBinder, queryable, null);

            Assert.IsNotNull(linqIndex, "Can't get an extended linqToSolrIndex...");

            var translatedFieldNames = fieldNames.Select(field => linqIndex.Parameters.FieldNameTranslator.GetIndexFieldName(field)).ToArray();

            GetResultsOptions options = GetResultsOptions.Default;
            var intermidQueryWithHighlighting = new SolrCompositeQueryWithHighlights(intermidQuery, options)
            {
                HighlightParameters = translatedFieldNames,
                Snippets = maxnumberofaccurencies,
                Htmltag = htmltag,
                FragmentSize = surroundingcharacters
            }; 



            // Execute the resulting query
            return linqIndex.Execute<SearchResultsWithHighlights<T>>(intermidQueryWithHighlighting);
        }
    }
}