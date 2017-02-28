using Sitecore.ContentSearch;
using Sitecore.ContentSearch.SolrProvider;
using System;
using System.Collections.Generic;
using System.Linq;
using Sitecore.ContentSearch.Abstractions;
using Sitecore.ContentSearch.Linq;
using Sitecore.ContentSearch.Linq.Common;
using Sitecore.ContentSearch.Linq.Methods;
using Sitecore.ContentSearch.Pipelines.IndexingFilters;
using Sitecore.ContentSearch.Security;
using SolrNet;

namespace Sitecore.HighlightDemo.Solr
{
    public struct SolrSearchResults<TElement>
    {
        private readonly SolrSearchContext context;

        private readonly SolrQueryResults<Dictionary<string, object>> searchResults;

        private readonly SolrIndexConfiguration solrIndexConfiguration;

        private readonly IIndexDocumentPropertyMapper<Dictionary<string, object>> mapper;

        private readonly SelectMethod selectMethod;

        private readonly IEnumerable<IExecutionContext> executionContexts;

        private readonly IEnumerable<IFieldQueryTranslator> virtualFieldProcessors;

        private readonly int numberFound;

        public SolrSearchResults(SolrSearchContext context, SolrQueryResults<Dictionary<string, object>> searchResults, SelectMethod selectMethod, IEnumerable<IExecutionContext> executionContexts, IEnumerable<IFieldQueryTranslator> virtualFieldProcessors)
        {
            this.context = context;
            this.solrIndexConfiguration = (SolrIndexConfiguration)this.context.Index.Configuration;
            this.selectMethod = selectMethod;
            this.virtualFieldProcessors = virtualFieldProcessors;
            this.executionContexts = executionContexts;
            this.numberFound = searchResults.NumFound;
            this.searchResults = ApplySecurity(searchResults, context.SecurityOptions, context.Index.Locator.GetInstance<Sitecore.Abstractions.ICorePipeline>(), context.Index.Locator.GetInstance<Sitecore.Abstractions.IAccessRight>(), ref this.numberFound);

            var executionContext = this.executionContexts != null ? this.executionContexts.FirstOrDefault(c => c is OverrideExecutionContext<IIndexDocumentPropertyMapper<Dictionary<string, object>>>) as OverrideExecutionContext<IIndexDocumentPropertyMapper<Dictionary<string, object>>> : null;
            this.mapper = (executionContext != null ? executionContext.OverrideObject : null) ?? solrIndexConfiguration.IndexDocumentPropertyMapper;
        }

        private static SolrQueryResults<Dictionary<string, object>> ApplySecurity(SolrQueryResults<Dictionary<string, object>> solrQueryResults, SearchSecurityOptions options, Sitecore.Abstractions.ICorePipeline pipeline, Sitecore.Abstractions.IAccessRight accessRight, ref int numberFound)
        {
            if (!options.HasFlag(SearchSecurityOptions.DisableSecurityCheck))
            {
                var removalList = new HashSet<Dictionary<string, object>>();

                foreach (var searchResult in solrQueryResults.Where(searchResult => searchResult != null))
                {
                    object secToken;
                    object dataSource;

                    if (!searchResult.TryGetValue(BuiltinFields.UniqueId, out secToken))
                    {
                        continue;
                    }

                    searchResult.TryGetValue(BuiltinFields.DataSource, out dataSource);

                    var isExcluded = OutboundIndexFilterPipeline.CheckItemSecurity(pipeline, accessRight, new OutboundIndexFilterArgs((string)secToken, (string)dataSource));

                    if (isExcluded)
                    {
                        removalList.Add(searchResult);
                        numberFound = numberFound - 1;
                    }
                }

                foreach (var item in removalList)
                {
                    solrQueryResults.Remove(item);
                }
            }

            return solrQueryResults;
        }

        public TElement ElementAt(int index)
        {
            if (index < 0 || index > this.searchResults.Count)
            {
                throw new IndexOutOfRangeException();
            }

            return this.mapper.MapToType<TElement>(this.searchResults[index], this.selectMethod, this.virtualFieldProcessors, this.executionContexts, this.context.SecurityOptions);
        }

        public TElement ElementAtOrDefault(int index)
        {
            if (index < 0 || index > this.searchResults.Count)
            {
                return default(TElement);
            }

            return this.mapper.MapToType<TElement>(this.searchResults[index], this.selectMethod, this.virtualFieldProcessors, this.executionContexts, this.context.SecurityOptions);
        }

        public bool Any()
        {
            return this.numberFound > 0;
        }

        public long Count()
        {
            return this.numberFound;
        }

        public TElement First()
        {
            if (this.searchResults.Count < 1)
            {
                throw new InvalidOperationException("Sequence contains no elements");
            }

            return this.ElementAt(0);
        }

        public TElement FirstOrDefault()
        {
            if (this.searchResults.Count < 1)
            {
                return default(TElement);
            }

            return this.ElementAt(0);
        }

        public TElement Last()
        {
            if (this.searchResults.Count < 1)
            {
                throw new InvalidOperationException("Sequence contains no elements");
            }

            return this.ElementAt(this.searchResults.Count - 1);
        }

        public TElement LastOrDefault()
        {
            if (this.searchResults.Count < 1)
            {
                return default(TElement);
            }

            return this.ElementAt(this.searchResults.Count - 1);
        }

        public TElement Single()
        {
            if (this.Count() < 1)
            {
                throw new InvalidOperationException("Sequence contains no elements");
            }

            if (this.Count() > 1)
            {
                throw new InvalidOperationException("Sequence contains more than one element");
            }

            return this.mapper.MapToType<TElement>(this.searchResults[0], this.selectMethod, this.virtualFieldProcessors, this.executionContexts, this.context.SecurityOptions);
        }

        public TElement SingleOrDefault()
        {
            if (this.Count() == 0)
            {
                return default(TElement);
            }

            if (this.Count() == 1)
            {
                return this.mapper.MapToType<TElement>(this.searchResults[0], this.selectMethod, this.virtualFieldProcessors, this.context.SecurityOptions);
            }

            throw new InvalidOperationException("Sequence contains more than one element");
        }

        public IEnumerable<SearchHit<TElement>> GetSearchHits()
        {
            foreach (var searchResult in this.searchResults)
            {
                float score = -1;

                object scoreObj;

                if (searchResult.TryGetValue("score", out scoreObj))
                {
                    if (scoreObj is float)
                    {
                        score = (float)scoreObj;
                    }
                }

                yield return new SearchHit<TElement>(score, this.mapper.MapToType<TElement>(searchResult, this.selectMethod, this.virtualFieldProcessors, this.executionContexts, this.context.SecurityOptions));
            }
        }

        public IEnumerable<TElement> GetSearchResults()
        {
            foreach (var searchResult in this.searchResults)
            {
                yield return this.mapper.MapToType<TElement>(searchResult, this.selectMethod, this.virtualFieldProcessors, this.executionContexts, this.context.SecurityOptions);
            }
        }

        public Dictionary<string, ICollection<KeyValuePair<string, int>>> GetFacets()
        {
            IDictionary<string, ICollection<KeyValuePair<string, int>>> facetFields = searchResults.FacetFields;
            IDictionary<string, IList<Pivot>> pivotFacets = searchResults.FacetPivots;

            var finalresults = facetFields.ToDictionary(x => x.Key, x => x.Value);

            if (pivotFacets.Count > 0)
            {
                foreach (var pivotFacet in pivotFacets)
                {
                    finalresults[pivotFacet.Key] = Flatten(pivotFacet.Value, string.Empty);
                }
            }

            return finalresults;
        }

        public int NumberFound
        {
            get
            {
                return this.numberFound;
            }
        }

        private ICollection<KeyValuePair<string, int>> Flatten(IEnumerable<Pivot> pivots, string parentName)
        {
            var keys = new HashSet<KeyValuePair<string, int>>();

            foreach (var pivot in pivots)
            {
                if (parentName != string.Empty)
                {
                    keys.Add(new KeyValuePair<string, int>(parentName + "/" + pivot.Value, pivot.Count));
                }

                if (pivot.HasChildPivots)
                {
                    keys.UnionWith(this.Flatten(pivot.ChildPivots, pivot.Value));
                }
            }

            return keys;
        }
    }
}