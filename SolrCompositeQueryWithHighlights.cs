using Sitecore.ContentSearch.Linq;
using Sitecore.ContentSearch.Linq.Methods;
using Sitecore.ContentSearch.Linq.Solr;

namespace Sitecore.HighlightDemo.Solr
{
    public class SolrCompositeQueryWithHighlights : SolrCompositeQuery
    {
        public SolrCompositeQueryWithHighlights(SolrCompositeQuery query, GetResultsOptions options = GetResultsOptions.Default)
            : base(query.Query, query.Filter, query.Methods, query.VirtualFieldProcessors, query.FacetQueries, query.ExecutionContexts)
        {
            this.Methods.Insert(0, new GetResultsMethod(options));
        }

        // Can contain extra parameters. For the example, field names are stored only
        public string[] HighlightParameters { set; get; }
        public int Snippets;
        public string Htmltag;
        public int FragmentSize; 
    }
}
