# A C# .NET Core Route Engine

A simple route-to-function engine for use with .NET Core web application.

Features:

* Route-to-Function Execution
* Route Specific Middleware
* General Route Middleware
* Global Middleware
* Serving Static Content
* Route Cache for Quicker Lookup

## Setup

```csharp
var host = new WebHostBuilder()
  .UseKestrel()
  .UseIISIntegration()
  .UseStartup<FloatEngine>()
  .Build();
```

## The FloatRouteContext Class

The FloatRouteContext class is the object which is passed around through all global middleware, general route middleware, route specific middleware, and finally the route function.

The class contains the following properties:

* Request (HttpRequest)
* Headers (IHeaderDictionary)
* Parameters (Dictionary(string, string))
* Objects (Dictionary(string, object))
* Body (string)

and the following functions:

* BodyTo

### Request

This is the actual HttpRequest from the .NET Core web request. It has all the goodies this framework doesn't provide.

### Headers

Headers from the actual request.

### Parameters

A list of parameters from the URL, e.g.: ```/api/v1/user/{id}```

In this case a parameter with the name ```id``` would be in the list, with the value from the URL, e.g.: 3 or something.

### Objects

A list of objects that can be passed between all middlewares and lastly the executing function.

### Body

The request body, in string format.

### BodyTo

A function you can call with a class to cast the body to that class, e.g.: ```var user = context.BodyTo<User>();```

## Route-to-Function Execution

```csharp
// Put this is your start-up somewhere.
FloatEngine.RegisterRouteFunction("api/v1/user", HttpMethod.Post, CreateUser);
FloatEngine.RegisterRouteFunction("api/v1/user/{id}", HttpMethod.Get, GetUser);

// This is the function executed for api/v1/user
public static CreateUser(FloatRouteContext context) {
	// Do you magic inside here.
	// You can either return a class, which will automatically be
	// serialized to JSON, or you can return a FloatRouteResponse
	// class where you can specify status-code, headers, and body.
}

// This is the function executed for api/v1/user/{id}
public static GetUser(FloatRouteContext context) {
	int id;
	
	if (context.Parameters["id"] == null ||
		!int.TryParse(context.Parameters["id"], out id) {
		throw new FloatRouteException(400);
	}
}
```

## Route Specific Middleware

## General Route Middleware

## Global Middleware

## Serving Static Content

## Route Cache for Quicker Lookup

## Use It to Serve HTML on /