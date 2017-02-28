using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Xml;
using Sitecore.Configuration;
using Sitecore.ContentSearch;
using Sitecore.ContentSearch.Abstractions;
using Sitecore.ContentSearch.Diagnostics;
using Sitecore.ContentSearch.Linq;
using Sitecore.ContentSearch.Linq.Common;
using Sitecore.ContentSearch.Linq.Methods;
using Sitecore.ContentSearch.Linq.Nodes;
using Sitecore.ContentSearch.Linq.Solr;
using Sitecore.ContentSearch.Pipelines.GetFacets;
using Sitecore.ContentSearch.Pipelines.ProcessFacets;
using Sitecore.ContentSearch.Security;
using Sitecore.ContentSearch.SolrProvider;
using Sitecore.ContentSearch.SolrProvider.Logging;
using Sitecore.ContentSearch.Utilities;
using Sitecore.Diagnostics;
using SolrNet;
using SolrNet.Commands.Parameters;
using SolrNet.Exceptions;

namespace Sitecore.HighlightDemo.Solr
{
    public class LinqToSolrIndexExtended<TItem> : SolrIndex<TItem>
    {
        private readonly SolrSearchContext context;

        private readonly string cultureCode;

        private readonly IContentSearchConfigurationSettings contentSearchSettings;

        private readonly Sitecore.Abstractions.ICorePipeline pipeline;

        public LinqToSolrIndexExtended([NotNull]SolrSearchContext context, IExecutionContext executionContext)
            : this(context, new[] { executionContext })
        {

        }

        private TResult ApplyScalarMethods<TResult, TDocument>(SolrCompositeQuery compositeQuery, SolrSearchResults<TDocument> processedResults, SolrQueryResults<Dictionary<string, object>> results)
        {
            var method = compositeQuery.Methods.First();

            object result;

            switch (method.MethodType)
            {
                case QueryMethodType.All:
                    result = true;
                    break;

                case QueryMethodType.Any:
                    result = processedResults.Any();
                    break;

                case QueryMethodType.Count:
                    result = processedResults.Count();
                    break;

                case QueryMethodType.ElementAt:
                    if (((ElementAtMethod)method).AllowDefaultValue)
                    {
                        result = processedResults.ElementAtOrDefault(((ElementAtMethod)method).Index);
                    }
                    else
                    {
                        result = processedResults.ElementAt(((ElementAtMethod)method).Index);
                    }

                    break;

                case QueryMethodType.First:
                    if (((FirstMethod)method).AllowDefaultValue)
                    {
                        result = processedResults.FirstOrDefault();
                    }
                    else
                    {
                        result = processedResults.First();
                    }

                    break;

                case QueryMethodType.Last:
                    if (((LastMethod)method).AllowDefaultValue)
                    {
                        result = processedResults.LastOrDefault();
                    }
                    else
                    {
                        result = processedResults.Last();
                    }

                    break;

                case QueryMethodType.Single:
                    if (((SingleMethod)method).AllowDefaultValue)
                    {
                        result = processedResults.SingleOrDefault();
                    }
                    else
                    {
                        result = processedResults.Single();
                    }

                    break;

                case QueryMethodType.GetResults:
                    var resultList = processedResults.GetSearchHits();
                    var facets = this.FormatFacetResults(processedResults.GetFacets(), compositeQuery.FacetQueries);
                    result = ReflectionUtility.CreateInstance(typeof(TResult), resultList, processedResults.NumberFound, facets); // Create instance of SearchResults<TDocument>
                    break;


                case QueryMethodType.GetFacets:
                    result = this.FormatFacetResults(processedResults.GetFacets(), compositeQuery.FacetQueries);
                    break;

                default:
                    throw new InvalidOperationException("Invalid query method");
            }

            return (TResult)System.Convert.ChangeType(result, typeof(TResult));
        }

        public override IEnumerable<TElement> FindElements<TElement>(SolrCompositeQuery compositeQuery)
        {
            var results = this.Execute(compositeQuery, typeof(TElement));

            var selectMethods = compositeQuery.Methods.Where(m => m.MethodType == QueryMethodType.Select).Select(m => (SelectMethod)m).ToList();

            var selectMethod = selectMethods.Count() == 1 ? selectMethods[0] : null;

            var processedResults = new SolrSearchResults<TElement>(this.context, results, selectMethod, compositeQuery.ExecutionContexts, compositeQuery.VirtualFieldProcessors);

            return processedResults.GetSearchResults();
        }

        private FacetResults FormatFacetResults(Dictionary<string, ICollection<KeyValuePair<string, int>>> facetResults, List<FacetQuery> facetQueries)
        {
            var fieldTranslator = this.context.Index.FieldNameTranslator as SolrFieldNameTranslator;
            var processedFacets = ProcessFacetsPipeline.Run(this.pipeline, new ProcessFacetsArgs(facetResults, facetQueries, facetQueries, this.context.Index.Configuration.VirtualFieldProcessors, fieldTranslator));

            foreach (var originalQuery in facetQueries)
            {
                if (originalQuery.FilterValues == null || !originalQuery.FilterValues.Any())
                {
                    continue;
                }

                if (!processedFacets.ContainsKey(originalQuery.CategoryName))
                {
                    continue;
                }

                var categoryValues = processedFacets[originalQuery.CategoryName];
                processedFacets[originalQuery.CategoryName] = categoryValues.Where(cv => originalQuery.FilterValues.Contains(cv.Key)).ToList();
            }

            var facetFormattedResults = new FacetResults();

            foreach (var group in processedFacets)
            {
                if (fieldTranslator == null)
                {
                    continue;
                }

                var key = group.Key;

                if (key.Contains(","))
                {
                    key = fieldTranslator.StripKnownExtensions(key.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                }
                else
                {
                    key = fieldTranslator.StripKnownExtensions(key);
                }

                var values = @group.Value.Select(v => new FacetValue(v.Key, v.Value));
                facetFormattedResults.Categories.Add(new FacetCategory(key, values));
            }

            return facetFormattedResults;
        }

        private static SelectMethod GetSelectMethod(SolrCompositeQuery compositeQuery)
        {
            var selectMethods = compositeQuery.Methods.Where(m => m.MethodType == QueryMethodType.Select).Select(m => (SelectMethod)m).ToList();

            return selectMethods.Count() == 1 ? selectMethods[0] : null;
        }

        // CUSTOM CODE

        private readonly PropertyInfo indexOperationsInfo;

        public LinqToSolrIndexExtended([NotNull] SolrSearchContext context, IExecutionContext[] executionContexts)
            : base(
                new SolrIndexParameters(
                    context.Index.Configuration.IndexFieldStorageValueFormatter,
                    context.Index.Configuration.VirtualFields,
                    context.Index.FieldNameTranslator,
                    executionContexts[0]))
        {
            Assert.ArgumentNotNull(context, "context");
            this.context = context;

            contentSearchSettings = context.Index.Locator.GetInstance<IContentSearchConfigurationSettings>();
            pipeline = context.Index.Locator.GetInstance<Sitecore.Abstractions.ICorePipeline>();

            var cultureExecutionContext = this.Parameters.ExecutionContexts.FirstOrDefault(c => c is CultureExecutionContext) as CultureExecutionContext;

            var culture = cultureExecutionContext == null
                               ? CultureInfo.GetCultureInfo(Settings.DefaultLanguage)
                               : cultureExecutionContext.Culture;

            this.cultureCode = culture.TwoLetterISOLanguageName;

            ((SolrFieldNameTranslator)this.Parameters.FieldNameTranslator).AddCultureContext(culture);

            var curType = this.context.Index.GetType();

            // TODO Check it e.g. when interfaces exist
            while (curType != typeof(Sitecore.ContentSearch.SolrProvider.SolrSearchIndex) && curType.BaseType != typeof(System.Object))
            {
                curType = curType.BaseType;
            }

            if (curType != typeof(Sitecore.ContentSearch.SolrProvider.SolrSearchIndex))
            {
                throw new InvalidOperationException("Can't get the SolrSearchIndex type...");
            }

            indexOperationsInfo = curType.GetProperty("SolrOperations", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private ISolrOperations<Dictionary<string, object>> GetOperations(SolrSearchIndex index)
        {
            return indexOperationsInfo.GetValue(index) as ISolrOperations<Dictionary<string, object>>;
        }

        public override TResult Execute<TResult>(SolrCompositeQuery compositeQuery)
        {
            var queryWithHighlighting = compositeQuery as SolrCompositeQueryWithHighlights;
            bool resWithHighlights = queryWithHighlighting != null && typeof(TResult).GetGenericTypeDefinition() == typeof(SearchResultsWithHighlights<>);

            // TODO Check this condition in more details
            if (typeof(TResult).IsGenericType && (typeof(TResult).GetGenericTypeDefinition() == typeof(SearchResults<>) || resWithHighlights))
            {
                var documentType = typeof(TResult).GetGenericArguments()[0];
                var results = this.Execute(compositeQuery, documentType);

                var solrSearchResultsType = typeof(SolrSearchResults<>);
                var solrSearchResultsGenericType = solrSearchResultsType.MakeGenericType(documentType);

                var applyScalarMethodsMethod = this.GetType().GetMethod("ApplyScalarMethods", BindingFlags.Instance | BindingFlags.NonPublic);

                // We need to handle the search result for the GetResultsWithHighlights as for the default GetResults case:
                Type returnType = (resWithHighlights) ? typeof(SearchResults<>).MakeGenericType(documentType) : typeof(TResult);
                var applyScalarMethodsGenericMethod = applyScalarMethodsMethod.MakeGenericMethod(returnType, documentType);

                var selectMethod = GetSelectMethod(compositeQuery);

                // Execute query methods
                var processedResults = ReflectionUtility.CreateInstance(solrSearchResultsGenericType, this.context, results, selectMethod, compositeQuery.ExecutionContexts, compositeQuery.VirtualFieldProcessors);

                var searchResult = applyScalarMethodsGenericMethod.Invoke(this, new object[] { compositeQuery, processedResults, results });

                if (resWithHighlights)
                {
                    return (TResult)ReflectionUtility.CreateInstance(typeof(TResult), searchResult, results.Highlights);
                }

                return (TResult)searchResult;
            }
            else
            {
                var results = this.Execute(compositeQuery, typeof(TResult));

                var selectMethod = GetSelectMethod(compositeQuery);

                var processedResults = new SolrSearchResults<TResult>(this.context, results, selectMethod, compositeQuery.ExecutionContexts, compositeQuery.VirtualFieldProcessors);

                return this.ApplyScalarMethods<TResult, TResult>(compositeQuery, processedResults, results);
            }
        }

        internal SolrQueryResults<Dictionary<string, object>> Execute(SolrCompositeQuery compositeQuery, Type resultType)
        {
            var queryOpertions = new QueryOptions();

            if (compositeQuery.Methods != null)
            {
                var selectFields = compositeQuery.Methods.Where(m => m.MethodType == QueryMethodType.Select).Select(m => (SelectMethod)m).ToList();

                if (selectFields.Any())
                {
                    foreach (var fieldName in selectFields.SelectMany(selectMethod => selectMethod.FieldNames))
                    {
                        queryOpertions.Fields.Add(fieldName.ToLowerInvariant());
                    }

                    if (!this.context.SecurityOptions.HasFlag(SearchSecurityOptions.DisableSecurityCheck))
                    {
                        queryOpertions.Fields.Add(BuiltinFields.UniqueId);
                        queryOpertions.Fields.Add(BuiltinFields.DataSource);
                    }
                }

                var getResultsFields = compositeQuery.Methods.Where(m => m.MethodType == QueryMethodType.GetResults).Select(m => (GetResultsMethod)m).ToList();

                if (getResultsFields.Any())
                {
                    if (queryOpertions.Fields.Count > 0)
                    {
                        queryOpertions.Fields.Add("score");
                    }
                    else
                    {
                        queryOpertions.Fields.Add("*");
                        queryOpertions.Fields.Add("score");
                    }
                }

                var sortFields = compositeQuery.Methods.Where(m => m.MethodType == QueryMethodType.OrderBy).Select(m => ((OrderByMethod)m)).ToList();

                if (sortFields.Any())
                {
                    foreach (var sortField in sortFields)
                    {
                        var fieldName = sortField.Field;
                        queryOpertions.AddOrder(new SortOrder(fieldName, sortField.SortDirection == SortDirection.Ascending ? Order.ASC : Order.DESC));
                    }
                }

                var skipFields = compositeQuery.Methods.Where(m => m.MethodType == QueryMethodType.Skip).Select(m => (SkipMethod)m).ToList();

                if (skipFields.Any())
                {
                    var start = skipFields.Sum(skipMethod => skipMethod.Count);
                    queryOpertions.Start = start;
                }

                var takeFields = compositeQuery.Methods.Where(m => m.MethodType == QueryMethodType.Take).Select(m => (TakeMethod)m).ToList();

                if (takeFields.Any())
                {
                    var rows = takeFields.Sum(takeMethod => takeMethod.Count);
                    queryOpertions.Rows = rows;
                }

                var countFields = compositeQuery.Methods.Where(m => m.MethodType == QueryMethodType.Count).Select(m => (CountMethod)m).ToList();

                if (compositeQuery.Methods.Count == 1 && countFields.Any())
                {
                    queryOpertions.Rows = 0;
                }

                var anyFields = compositeQuery.Methods.Where(m => m.MethodType == QueryMethodType.Any).Select(m => (AnyMethod)m).ToList();

                if (compositeQuery.Methods.Count == 1 && anyFields.Any())
                {
                    queryOpertions.Rows = 0;
                }

                var facetFields = compositeQuery.Methods.Where(m => m.MethodType == QueryMethodType.GetFacets).Select(m => (GetFacetsMethod)m).ToList();

                if (compositeQuery.FacetQueries.Count > 0 && (facetFields.Any() || getResultsFields.Any()))
                {
                    var result = GetFacetsPipeline.Run(this.pipeline, new GetFacetsArgs(null, compositeQuery.FacetQueries, this.context.Index.Configuration.VirtualFieldProcessors, this.context.Index.FieldNameTranslator));
                    var facetQueries = result.FacetQueries.ToHashSet();

                    foreach (var facetQuery in facetQueries)
                    {
                        if (!facetQuery.FieldNames.Any())
                        {
                            continue;
                        }

                        var minCount = facetQuery.MinimumResultCount;

                        if (facetQuery.FieldNames.Count() == 1)
                        {
                            var fn = FieldNameTranslator as SolrFieldNameTranslator;
                            var fieldName = facetQuery.FieldNames.First();

                            if (fn != null && fieldName == fn.StripKnownExtensions(fieldName) && this.context.Index.Configuration.FieldMap.GetFieldConfiguration(fieldName) == null)
                            {
                                fieldName = fn.GetIndexFieldName(fieldName.Replace("__", "!").Replace("_", " ").Replace("!", "__"), true);
                            }

                            queryOpertions.AddFacets(new SolrFacetFieldQuery(fieldName) { MinCount = minCount });
                        }

                        if (facetQuery.FieldNames.Count() > 1)
                        {
                            queryOpertions.AddFacets(new SolrFacetPivotQuery { Fields = new[] { string.Join(",", facetQuery.FieldNames) }, MinCount = minCount });
                        }
                    }

                    if (!getResultsFields.Any())
                    {
                        queryOpertions.Rows = 0;
                    }
                }
            }

            if (compositeQuery.Filter != null)
            {
                queryOpertions.AddFilterQueries(compositeQuery.Filter);
            }

            queryOpertions.AddFilterQueries(new SolrQueryByField(BuiltinFields.IndexName, this.context.Index.Name));

            if (!Settings.DefaultLanguage.StartsWith(this.cultureCode))
            {
                queryOpertions.AddFilterQueries(new SolrQueryByField(BuiltinFields.Language, this.cultureCode + "*") { Quoted = false });
            }

            var querySerializer = new SolrLoggingSerializer();
            var serializedQuery = querySerializer.SerializeQuery(compositeQuery.Query);

            var idx = this.context.Index as SolrSearchIndex;

            PrepareHiglightOptions(compositeQuery, queryOpertions);

            try
            {
                if (queryOpertions.Rows == null)
                {
                    queryOpertions.Rows = this.contentSearchSettings.SearchMaxResults();
                }

                SearchLog.Log.Info("Query - " + serializedQuery);
                SearchLog.Log.Info("Serialized Query - " + "?q=" + serializedQuery + "&" + string.Join("&", querySerializer.GetAllParameters(queryOpertions).Select(p => string.Format("{0}={1}", p.Key, p.Value)).ToArray()));

                return GetOperations(idx).Query(serializedQuery, queryOpertions);
            }
            catch (Exception exception)
            {
                if (!(exception is SolrConnectionException) && !(exception is SolrNetException))
                {
                    throw;
                }

                var message = exception.Message;

                if (exception.Message.StartsWith("<?xml"))
                {
                    var doc = new XmlDocument();
                    doc.LoadXml(exception.Message);
                    var errorNode = doc.SelectSingleNode("/response/lst[@name='error'][1]/str[@name='msg'][1]");
                    var queryNode = doc.SelectSingleNode("/response/lst[@name='responseHeader'][1]/lst[@name='params'][1]/str[@name='q'][1]");
                    if (errorNode != null && queryNode != null)
                    {
                        message = string.Format("Solr Error : [\"{0}\"] - Query attempted: [{1}]", errorNode.InnerText, queryNode.InnerText);
                        SearchLog.Log.Error(message);
                        return new SolrQueryResults<Dictionary<string, object>>();
                    }
                }

                Log.Error(message, this);
                return new SolrQueryResults<Dictionary<string, object>>();
            }
        }

        private void PrepareHiglightOptions(SolrCompositeQuery query, QueryOptions options)
        {
            var extQuery = query as SolrCompositeQueryWithHighlights;
            if (extQuery == null || extQuery.HighlightParameters == null)
            {
                return;
            }

            var highlightOptions = new HighlightingParameters();

            highlightOptions.Fields = extQuery.HighlightParameters;
            highlightOptions.Snippets = extQuery.Snippets;
            highlightOptions.BeforeTerm = "<" + extQuery.Htmltag + ">";
            highlightOptions.AfterTerm = "</" + extQuery.Htmltag + ">";
            highlightOptions.Fragsize = extQuery.FragmentSize;
            options.Highlight = highlightOptions;
            
        }

    }
} 
