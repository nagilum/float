# A C# .NET Core Route Engine

A simple async route-to-function engine for use with .NET Core web application.

Features:

* [Route-to-Function Execution](#user-content-route-to-function-execution)
* [Route Specific Middleware](#user-content-route-specific-middleware)
* [Global Middleware](#user-content-global-middleware)
* [Serving Static Content](#user-content-serving-static-content)
* [Route Cache for Quicker Lookup](#user-content-route-cache-for-quicker-lookup)
* [Use It to Serve HTML on /](#user-content-use-it-to-serve-html-on-)

## Setup

```
// Here you replace the ```UseStartup<Startup>``` with
// FloatEngine's own.
var host = new WebHostBuilder()
  .UseKestrel()
  .UseIISIntegration()
  .UseStartup<FloatEngine>() // This is the magic!
  .Build();
```

## Classes You Need to Know About

* [FloatRouteContext](#user-content-the-floatroutecontext-class)
* [FloatRouteException](#user-content-the-floatrouteexception-class)
* [FloatRouteResponse](#user-content-the-floatrouteresponse-class)

## The FloatRouteContext Class

The FloatRouteContext class is the object which is passed around through all global middleware, route specific middleware, and finally the route function.

The class contains the following properties:

* Request (HttpRequest)
* Headers (IHeaderDictionary)
* Parameters (Dictionary(string, string))
* Objects (Dictionary(string, object))
* Body (string)

and the following function:

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

## The FloatRouteException Class

This is the preferred exception to throw while in middleware and the main functions. It holds what you need to give a proper reply when an error occurs.

The class contains the following properties:

* StatusCode (int)
* ErrorMessage (string)
* Payload (object)

### StatusCode

The status code for the HTTP response.

### ErrorMessage

If you supply an error message, the response body will be a JSON object, as such:

```
{
  "errorMessage": "your message here"
}
```

### Payload

If you supply a payload, the entirety of it will be a serialized string of it.

### How To Use

```
// Throw a single 404 error.
throw new FloatRouteException(404);

// Throw a 400 error with a message.
throw new FloatRouteException(400, "You did something wrong");

// Throw a 401 error with a payload.
throw new FloatRouteException(401, new { YO = "MAMA" });
```

## The FloatRouteResponse Class

In the route function you can either return any class, or more specifically, a FloatRouteResponse class. The FloatRouteResponse class will be routed directly to the response engine.

The class contains the following properties:

* StatusCode (int)
* Headers (Dictionary (string, string))
* Body (string)

### StatusCode

This is the status code that will be part of the HTTP response.

### Headers

Here you can put various headers you wish to be added to the response.

### Body

The string that will be the response body.

### How To Use

```
// In one of your route functions, just return the
// FloatRouteResponse class, as such
return new FloatRouteResponse {
  StatusCode = 200,
  Headers = new Dictionary<string, string> {
    {"Content-Type", "text/html"}
  },
  Body = "<!doctype html><html></html>"
};
```

## Route-to-Function Execution

The whole point of the framework is to connect routes to functions, making it easy for you, the developer, to concentrate on the actual content being served.

```
// Put this is your start-up somewhere.
FloatEngine.RegisterRouteFunction(
  "api/v1/user",
  FloatHttpMethod.POST,
  CreateUser);

FloatEngine.RegisterRouteFunction(
  "api/v1/user/{id}",
  FloatHttpMethod.GET,
  GetUser);

// This is the function executed for api/v1/user
public static CreateUser(FloatRouteContext context) {
  // Do you magic inside here. You can either return a class,
  // which will automatically be serialized to JSON, or you can
  // return a FloatRouteResponse class where you can specify
  // status-code, headers, and body.
}

// This is the function executed for api/v1/user/{id}
public static GetUser(FloatRouteContext context) {
  int id;

  if (context.Parameters["id"] == null ||
      !int.TryParse(context.Parameters["id"], out id ||
      id == 0) {
        throw new FloatRouteException(400);
  }

  var user = DatabaseContext.Users.SingleOrDefault(u => u.ID == id);

  if (user == null) {
    throw new FloatRouteException(404);
  }
	
  return user;
}
```

## Route Specific Middleware

You can add middleware to be executed for each route.

```
// This will execute FirstMiddlewareFunction() first,
// then SecondMiddlewareFunction(), then GetUser().
FloatEngine.RegisterRouteFunction(
  "api/v1/user/{id}",
  FloatHttpMethod.GET,
  GetUser,
  new List<Action<FloatRouteContext>> {
    FirstMiddlewareFunction,
    SecondMiddlewareFunction
  });

// The middleware function also has the FloatRouteContext
// as its only parameter. It holds all you need to interject
// and do all sorts of stuff. If you throw an exception while
// in a middleware function, the route will be terminated and
// the exception will be returned.
public static FirstMiddlewareFunction(FloatRouteContext context) {
}
```

## Global Middleware

You can register middleware functions that will be executed before each route request.

```
// If you register more than one global middleware, they will
// be executed in the order they are added.
FloatEngine.RegisterGlobalMiddleware(MahGlobalMW);

// This will now be ran before all route functions and middleware.
public static void MahGlobalMW(FloatRouteContext context) {
}
```

## Serving Static Content

Sometimes you also might want to serve some content that doesn't need to go through a route. You can set up folders that will be served statically.

```
// This will register the remote folder "assets" to answer to
// the local folder "MyLocalAssets", meaning, if you request
// the file /assets/css/app.css, the local file
// MyLocalAssets\css\app.css will be served.
FloatEngine.RegisterStaticFolder(
  Directory.GetCurrentDirectory() + "\\MyLocalAssets",
  "assets");
```

## Route Cache for Quicker Lookup

When a route is executed the first time, the raw URL is added to a cache so the next time the route matching doesn't have to take place, which can save a fraction of the total request time.

## Use It to Serve HTML on /

If you want to serve content on ```/``` then you simply add a route as such:

```
FloatEngine.RegisterRouteFunction(
  "",
  FloatHttpMethod.GET,
  (context) => {
    return new FloatRouteResponse {
      StatusCode = 200,
      Headers = new Dictionary<string, string> {
        {"Content-Type", "text/html"}
      },
      Body = "<!doctype html><html></html>"
    }
  });
```