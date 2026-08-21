# DalSoft .NET REST Client for all platforms

### `If you find this repo / package useful all I ask is you please star it ⭐`
> ### Do you or the company you work for benefit from the tools I build? <br /> If so please consider [Becoming a Sponsor](https://github.com/sponsors/dalsoft) it would be greatly appreciated ❤️ 

![Nuget](https://img.shields.io/nuget/v/DalSoft.RestClient)
[![StackOverflow](https://img.shields.io/badge/questions-on%20StackOverflow-orange.svg?style=flat)](http://stackoverflow.com/questions/tagged/dalsoft.restclient)
[![Docs](https://img.shields.io/badge/Docs-Website-yellow)](https://restclient.dalsoft.io/)

> ## **For everything you need to know, please head over to [https://restclient.dalsoft.io](https://restclient.dalsoft.io)**
> ## **👉 New in 5.1: [call MCP servers like any other API](https://restclient.dalsoft.io/docs/mcphandler/) and [Typed Clients](https://restclient.dalsoft.io/docs/typed-clients/)**

![alt text](https://www.dalsoft.co.uk/blog/wp-content/uploads/2019/08/intellisense.gif)

## Just some of the things you can do with DalSoft.RestClient

* [Easily Create Fluent SDK's with Typed Clients](https://restclient.dalsoft.io/docs/typed-clients/)
* [Unit Testing](https://restclient.dalsoft.io/docs/unit-testing/)
* [Post Json](https://restclient.dalsoft.io/docs/put-post-patch/)
* [Post Forms](https://restclient.dalsoft.io/docs/formurlencodedhandler/)
* [Post Files](https://restclient.dalsoft.io/docs/multipartformdatahandler/)
* [Retry Requests](https://restclient.dalsoft.io/docs/retrying-transient-errors/)
* [Raw HTTP](https://restclient.dalsoft.io/docs/content-other-than-json/)
* [Passthrough HttpClient](https://restclient.dalsoft.io/docs/passthrough-httpclient/)
* [Authorization](https://restclient.dalsoft.io/docs/authorization/)


## Supported Platforms

Targets .NET Standard 2.0 and .NET 8.0 - .NET / .NET Core, .NET Framework 4.6.2+ and Xamarin (iOS, Android, UWP) on Windows, Linux and Mac. See [Supported Platforms](https://restclient.dalsoft.io/docs/supported-platforms/).

## Getting Started

## Install via .NET CLI

```bash
> dotnet add package DalSoft.RestClient
```

## Install via NuGet

```bash
PM> Install-Package DalSoft.RestClient
```

## Example calling a REST API 

You start by new'ing up the RestClient and passing in the base uri for your RESTful API. 

For example if your wanted to perform a GET on [https://jsonplaceholder.typicode.com/users/1](https://jsonplaceholder.typicode.com/users/1) you would do the following:

**Static Typed Rest Client**

For the Static typed Rest Client just pass a string representing the resource you want access to the Resource method, and then call the HTTP method you want to use. 
```cs
var client = new RestClient("https://jsonplaceholder.typicode.com");

User user = await client.Resource("users/1").Get();
   
Console.WriteLine(user.Name);
```

**Dynamicaly Typed Rest Client**

For the Dynamicaly typed Rest Client chain members that would make up the resource you want to access - ending with the HTTP method you want to use. 
```cs
dynamic client = new RestClient("https://jsonplaceholder.typicode.com");

var user = await client.Users(1).Get();
   
Console.WriteLine(user.name);
```
> Note all HTTP methods are async
 
## Recent Releases 
 
* Version 5.1 Typed clients via `AddRestClient<TClient>()` and an MCP (Model Context Protocol) handler - see below
* [Version 5.0 System.Text.Json by default and near-native performance](https://dalsoft.co.uk/blog/dalsoft-restclient-5-system-text-json-and-near-native-performance/)
* [Version 4.0 Static Typing and Resource Expressions](http://www.dalsoft.co.uk/blog/index.php/2019/08/04/csharp-rest-client-now-with-static-typing)
* [Version 3.3.0 IHttpClientFactory Goodness](https://restclient.dalsoft.io/docs/ihttpclientfactory/)
* [Version 3.0 Pipeline Awesomeness](https://restclient.dalsoft.io/docs/about-the-handler-pipeline/)

## About
RestClient is a very lightweight wrapper around System.Net.HttpClient that uses the dynamic features of .NET 4 to provide a fluent way of accessing RESTFul API's, making it trivial to create REST requests using a lot less code. 

Originally created to remove the boilerplate code involved in making REST requests using code that is testable. I know there are a couple of  REST clients out there but I wanted the syntax to look a particular way with minimal fuss.

RestClient is biased towards posting and returning JSON - if you don't provide Accept and Content-Type headers then they are set to application/json by default [See Working with non JSON content](https://restclient.dalsoft.io/docs/content-other-than-json/).


## What's new in 5.1 - Typed Clients and MCP

**Typed clients** - register a typed client the same way you would with `AddHttpClient<TClient>()` and take `IRestClient` in the constructor:

```cs
services.AddRestClient<GitHubClient>("https://api.github.com", new Headers(new { UserAgent = "MyClient" }));

public class GitHubClient
{
    private readonly IRestClient _restClient;

    public GitHubClient(IRestClient restClient) => _restClient = restClient;

    public Task<List<Repository>> GetRepositories(string user) =>
        _restClient.Resource($"users/{user}/repos").Get<List<Repository>>();
}
```

**MCP (Model Context Protocol)** - `UseMcpHandler()` lets you call MCP servers over the Streamable HTTP transport like any other API - the JSON-RPC envelope, session, SSE responses and errors are all handled for you:

```cs
IRestClient mcp = new RestClient("https://gateway.mcpservers.org/yahoo-finance/mcp", new Config().UseMcpHandler());

var tools = await mcp.ListTools();
var quotes = await mcp.CallToolJson("get_quote", new { symbols = new[] { "MSFT" } });
Console.WriteLine(quotes[0].regularMarketPrice);
```

TwitterHandler has been removed (the Twitter API v1.1 it targeted no longer exists). Full details: [Typed Clients](https://restclient.dalsoft.io/docs/typed-clients/) and [McpHandler](https://restclient.dalsoft.io/docs/mcphandler/).

## Version 5.0 - System.Text.Json and Near-Native Performance

Version 5.0 switched to [System.Text.Json](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/overview) with stock behaviour (`[JsonPropertyName]` honoured, case sensitive matching) and had a big performance pass - typed requests allocate the same as using HttpClient and System.Text.Json by hand, and about 6x less than RestSharp. If your models rely on the legacy Json.NET behaviour opt back in with `new Config().UseNewtonsoftJson()`.

Full details: [Upgrading to 5.0](https://restclient.dalsoft.io/docs/upgrading-to-5/), [Serialization](https://restclient.dalsoft.io/docs/serialization/) and [Performance](https://restclient.dalsoft.io/docs/performance/) (benchmarks are in the `DalSoft.RestClient.Benchmarks` project - `dotnet run -c Release`).

## Standing on the Shoulders of Giants

DalSoft.RestClient is built using the following great open source projects:
* [System.Text.Json](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/overview)
* [Json.NET](http://www.newtonsoft.com/json)
* [System.Net.Http](https://github.com/dotnet/corefx/tree/master/src/System.Net.Http)

DalSoft.RestClient is inspired by and gives credit to:
* [Simple.Data](http://simplefx.org/simpledata/docs/index.html)
* [This Stack Overflow question](http://stackoverflow.com/questions/12634250/possible-to-get-chained-value-of-dynamicobject)

