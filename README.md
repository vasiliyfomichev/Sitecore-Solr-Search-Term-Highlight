What is Sitecore Solr Search Term Highlighter?
==============================================

Demo project extending the Sitecore IQueriable to support search term highlighting in search results provided by Solr running on Sitecore.

How to use Sitecore Solr Search Term Highlighter?
-------------------------------------------------
Please read the [Search Term Highlighting in Sitecore with Solr](http://www.cmsbestpractices.com/highlight-search-terms-using-contentsearch-sitecore-solr/) blog post on how to use the module.

Example search query using the highlighter - 

'
using Sitecore.HighlightDemo.Solr; 
...
using (var context = index.CreateSearchContext(SearchSecurityOptions.DisableSecurityCheck))
{
var result = context.GetExtendedQueryable<SearchResultItem>().Where(it => it["definition"] == "panda").GetResultsWithHighlights("definition");
var highlights = result.Highlights;
}
'


Contributing
----------------------
If you have have a contribution for this repository, please send a pull request.


License
------------
The project has been developed under the MIT license.


Related Sitecore Projects
--------------------------------
- [Solr for Sitecore](https://github.com/vasiliyfomichev/solr-for-sitecore) - pre-built Solr Docker images ready to be used with Sitecore out of the box.
- [Sitecore ADFS Authenticator Module](https://github.com/vasiliyfomichev/Sitecore-ADFS-Authenticator-Module) - Sitecore module for authenticating users using ADFS.
- [Sitecore SignalR Tools](https://github.com/vasiliyfomichev/signalr-sitecore-tools) - commonly used Sitecore tools rebuilt using SignalR technology providing live updates and a modern interface.


Copyright 2015 Vasiliy Fomichev
