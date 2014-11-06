using System.Collections.Generic;
using Sitecore.ContentSearch.Linq;
using Sitecore.Diagnostics;
using SolrNet.Impl;

namespace Sitecore.HighlightDemo.Solr
{
    public class SearchResultsWithHighlights<T>
    {
        public SearchResultsWithHighlights(SearchResults<T> result, IDictionary<string, HighlightedSnippets> highlights)
        {
            Assert.ArgumentNotNull(result, "result");
            this.Results = result;
            this.Highlights = highlights ?? new Dictionary<string, HighlightedSnippets>();
        }

        public SearchResults<T> Results { get; private set; }
        public IDictionary<string, HighlightedSnippets> Highlights { get; private set; }
    }
}
