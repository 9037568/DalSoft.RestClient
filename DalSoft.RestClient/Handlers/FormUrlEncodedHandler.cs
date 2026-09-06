using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DalSoft.RestClient.Extensions;

namespace DalSoft.RestClient.Handlers
{
    public class FormUrlEncodedHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (IsFormUrlEncodedContentType(request))
            {
                var content = request.GetContent();
                request.Content = content == null ? null : new FormUrlEncodedContent
                (
                    content.FlattenToKeyValuePairs(includeThisType:Object.IsValueTypeOrPrimitiveOrStringOrGuidOrDateTime)
                        .Select(kvp => new KeyValuePair<string, string>(kvp.Key, kvp.Value?.FormatAsString()))
                );
            }

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false); //next in the pipeline
        }

        private static bool IsFormUrlEncodedContentType(HttpRequestMessage request)
        {
            return request.GetContentType() == "application/x-www-form-urlencoded";
        }
    }
}
