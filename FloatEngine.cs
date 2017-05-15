using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Float {
    public class FloatEngine {
        /// <summary>
        /// A list of global middleware.
        /// </summary>
        private static List<Action<FloatRouteContext>> GlobalMiddleware;

        /// <summary>
        /// A list of route cache entries.
        /// </summary>
        private static List<FloatRouteCacheEntry> RouteCache;

        /// <summary>
        /// A list of actual routes.
        /// </summary>
        private static List<FloatRoute> Routes;

        /// <summary>
        /// A list of mapped static folders.
        /// </summary>
        private static Dictionary<string, string> StaticFolder;

        #region Engine handlers

        /// <summary>
        /// Run the actual app in async mode.
        /// </summary>
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory lf) {
            app.Run(async (context) => {
                var res = await HandleRequest(context);

                if (res == null) {
                    return;
                }

                context.Response.StatusCode = res.StatusCode > 0
                    ? res.StatusCode
                    : 200;

                if (res.Headers != null) {
                    foreach (var header in res.Headers) {
                        context.Response.Headers.Add(header.Key, header.Value);
                    }
                }

                if (res.Body == null) {
                    res.Body = string.Empty;
                }

                await context.Response.WriteAsync(res.Body);
            });
        }

        /// <summary>
        /// Handle each request.
        /// </summary>
        public async Task<FloatRouteResponse> HandleRequest(HttpContext context) {
            var request = context.Request;

            if (!request.Path.HasValue) {
                return null;
            }

            var url = request.Path.Value;

            if (url.StartsWith("/")) {
                url = url.Substring(1);
            }

            // Check for static route.
            if (StaticFolder != null) {
                foreach (var item in StaticFolder) {
                    if (url.StartsWith(item.Key)) {
                        url = url.Substring(item.Key.Length);

                        var file = string.Format("{0}{1}",
                            item.Value,
                            url.Replace("/", "\\"));

                        if (File.Exists(file)) {
                            // TODO: Mime type?

                            await context.Response.SendFileAsync(file);
                            return null;
                        }

                        return new FloatRouteResponse {
                            StatusCode = 404
                        };
                    }
                }
            }

            // Handle route normally.
            if (Routes == null) {
                return null;
            }

            FloatRoute route = null;
            FloatRouteCacheEntry cache = null;
            FloatRouteResponse response = null;

            // Prepare the context.
            var ctx = new FloatRouteContext {
                IsLocal = request.Host.Host == "localhost",
                Request = request,
                Headers = request.Headers,
                Parameters = new Dictionary<string, string>(),
                Objects = new Dictionary<string, object>()
            };

            // Parse the body string.
            try {
                ctx.Body = new StreamReader(request.Body).ReadToEnd();
            }
            catch {
                // ignore
            }

            // Get route from cache.
            if (RouteCache != null) {
                cache = RouteCache
                    .FirstOrDefault(c => c.RouteUrl == url &&
                                         c.Method.ToString().Equals(request.Method, StringComparison.CurrentCultureIgnoreCase));

                if (cache != null) {
                    route = Routes.FirstOrDefault(r => r.Guid == cache.Guid);

                    if (cache.Parameters != null) {
                        ctx.Parameters = cache.Parameters;
                    }
                }
            }

            // Search for a matching route.
            if (route == null) {
                var sections = url.Split('/');
                var routes = Routes.Where(r => r.RouteSections.Length == sections.Length).ToList();

                if (!routes.Any()) {
                    return new FloatRouteResponse {
                        StatusCode = 404
                    };
                }

                routes = routes
                    .Where(r => r.Method.ToString().Equals(request.Method, StringComparison.CurrentCultureIgnoreCase))
                    .ToList();

                if (!routes.Any()) {
                    return new FloatRouteResponse {
                        StatusCode = 405
                    };
                }

                foreach (var temp in routes) {
                    var matches = 0;

                    for (var i = 0; i < sections.Length; i++) {
                        if (temp.RouteSections[i] == sections[i]) {
                            matches++;
                        }
                        else if (temp.RouteSections[i].StartsWith("{") &&
                                 temp.RouteSections[i].EndsWith("}")) {
                            ctx.Parameters.Add(
                                temp.RouteSections[i].Substring(1, temp.RouteSections[i].Length - 2),
                                sections[i]);

                            matches++;
                        }
                        else {
                            break;
                        }
                    }

                    if (matches != sections.Length) {
                        continue;
                    }

                    route = temp;
                    break;
                }
            }

            // No route found.
            if (route == null) {
                return new FloatRouteResponse {
                    StatusCode = 404
                };
            }

            // Cache route for quicker lookup later.
            if (cache == null) {
                if (RouteCache == null) {
                    RouteCache = new List<FloatRouteCacheEntry>();
                }

                RouteCache.Add(new FloatRouteCacheEntry {
                    RouteUrl = url,
                    Method = request.Method,
                    Guid = route.Guid,
                    Parameters = ctx.Parameters
                });
            }

            // Execute all global middleware.
            if (GlobalMiddleware != null) {
                foreach (var mw in GlobalMiddleware) {
                    try {
                        await Task.Run(() => mw.Invoke(ctx));
                    }
                    catch (FloatRouteException ex) {
                        response = new FloatRouteResponse {
                            StatusCode = ex.StatusCode,
                            Headers = new Dictionary<string, string> {
                                {"Content-Type", "application/json"}
                            }
                        };

                        if (!string.IsNullOrWhiteSpace(ex.ErrorMessage)) {
                            response.Body = JsonConvert.SerializeObject(new {
                                errorMessage = ex.ErrorMessage
                            });
                        }

                        if (ex.Payload != null) {
                            response.Body = JsonConvert.SerializeObject(ex.Payload);
                        }

                        if (string.IsNullOrWhiteSpace(response.Body) &&
                            !string.IsNullOrWhiteSpace(ex.Message)) {
                            response.Body = JsonConvert.SerializeObject(new {
                                errorMessage = ex.Message
                            });
                        }
                    }
                    catch (Exception ex) {
                        response = new FloatRouteResponse {
                            StatusCode = 500,
                            Headers = new Dictionary<string, string> {
                                {"Content-Type", "application/json"}
                            }
                        };

                        if (!string.IsNullOrWhiteSpace(ex.Message)) {
                            response.Body = JsonConvert.SerializeObject(new {
                                errorMessage = ex.Message
                            });
                        }
                    }

                    if (response != null) {
                        return response;
                    }
                }
            }

            // Execute all route specific middleware.
            if (route.Middleware != null) {
                foreach (var mw in route.Middleware) {
                    try {
                        await Task.Run(() => mw.Invoke(ctx));
                    }
                    catch (FloatRouteException ex) {
                        response = new FloatRouteResponse {
                            StatusCode = ex.StatusCode,
                            Headers = new Dictionary<string, string> {
                                {"Content-Type", "application/json"}
                            }
                        };

                        if (!string.IsNullOrWhiteSpace(ex.ErrorMessage)) {
                            response.Body = JsonConvert.SerializeObject(new {
                                errorMessage = ex.ErrorMessage
                            });
                        }

                        if (ex.Payload != null) {
                            response.Body = JsonConvert.SerializeObject(ex.Payload);
                        }

                        if (string.IsNullOrWhiteSpace(response.Body) &&
                            !string.IsNullOrWhiteSpace(ex.Message)) {
                            response.Body = JsonConvert.SerializeObject(new {
                                errorMessage = ex.Message
                            });
                        }
                    }
                    catch (Exception ex) {
                        response = new FloatRouteResponse {
                            StatusCode = 500,
                            Headers = new Dictionary<string, string> {
                                {"Content-Type", "application/json"}
                            }
                        };

                        if (!string.IsNullOrWhiteSpace(ex.Message)) {
                            response.Body = JsonConvert.SerializeObject(new {
                                errorMessage = ex.Message
                            });
                        }
                    }

                    if (response != null) {
                        return response;
                    }
                }
            }

            // Execute route function.
            try {
                var payload = await Task.Run(() => route.Function.Invoke(ctx));

                if (payload is FloatRouteResponse) {
                    response = payload as FloatRouteResponse;
                }
                else if (payload is string) {
                    response = new FloatRouteResponse {
                        StatusCode = 200,
                        Body = payload as string
                    };
                }
                else {
                    response = new FloatRouteResponse {
                        StatusCode = 200,
                        Headers = new Dictionary<string, string> {
                            {"Content-Type", "application/json"}
                        },
                        Body = JsonConvert.SerializeObject(payload)
                    };
                }
            }
            catch (FloatRouteException ex) {
                response = new FloatRouteResponse {
                    StatusCode = ex.StatusCode,
                    Headers = new Dictionary<string, string> {
                        {"Content-Type", "application/json"}
                    }
                };

                if (!string.IsNullOrWhiteSpace(ex.ErrorMessage)) {
                    response.Body = JsonConvert.SerializeObject(new {
                        errorMessage = ex.ErrorMessage
                    });
                }

                if (ex.Payload != null) {
                    response.Body = JsonConvert.SerializeObject(ex.Payload);
                }

                if (string.IsNullOrWhiteSpace(response.Body) &&
                    !string.IsNullOrWhiteSpace(ex.Message)) {
                    response.Body = JsonConvert.SerializeObject(new {
                        errorMessage = ex.Message
                    });
                }
            }
            catch (Exception ex) {
                response = new FloatRouteResponse {
                    StatusCode = 500,
                    Headers = new Dictionary<string, string> {
                        {"Content-Type", "application/json"}
                    }
                };

                if (!string.IsNullOrWhiteSpace(ex.Message)) {
                    response.Body = JsonConvert.SerializeObject(new {
                        errorMessage = ex.Message
                    });
                }
            }

            // Something has gone terribly wrong!
            return response ?? new FloatRouteResponse {
                StatusCode = 500,
                Headers = new Dictionary<string, string> {
                    {"Content-Type", "application/json"}
                },
                Body = JsonConvert.SerializeObject(new {
                    errorMessage = "Something has gone terribly wrong. Call Mom!"
                })
            };
        }

        #endregion

        #region Register functions

        /// <summary>
        /// Register a global middleware function.
        /// </summary>
        public static void RegisterGlobalMiddleware(Action<FloatRouteContext> function) {
            if (GlobalMiddleware == null) {
                GlobalMiddleware = new List<Action<FloatRouteContext>>();
            }

            GlobalMiddleware.Add(function);
        }

        /// <summary>
        /// Register a function execution route.
        /// </summary>
        public static void RegisterRouteFunction(string routeUrl, FloatHttpMethod method, Func<FloatRouteContext, object> function, List<Action<FloatRouteContext>> middleware = null) {
            if (Routes == null) {
                Routes = new List<FloatRoute>();
            }

            var guid = Guid.NewGuid().ToString();

            while (Routes.Count(r => r.Guid == guid) > 0) {
                guid = Guid.NewGuid().ToString();
            }

            Routes.Add(
                new FloatRoute {
                    Guid = guid,
                    Method = method,
                    RouteSections = routeUrl.Split('/'),
                    Function = function,
                    Middleware = middleware
                });
        }

        /// <summary>
        /// Register a static folder.
        /// </summary>
        public static void RegisterStaticFolder(string localPath, string remotePath) {
            if (StaticFolder == null) {
                StaticFolder = new Dictionary<string, string>();
            }

            if (localPath.StartsWith("~")) {
                localPath = Directory.GetCurrentDirectory() + localPath.Substring(1);
            }

            if (localPath.Contains("/")) {
                localPath = localPath.Replace("/", "\\");
            }

            StaticFolder.Add(remotePath, localPath);
        }

        #endregion

        #region Internal helper classes

        /// <summary>
        /// A route object.
        /// </summary>
        private class FloatRoute {
            /// <summary>
            /// Unique identifier.
            /// </summary>
            public string Guid { get; set; }

            /// <summary>
            /// HTTP method.
            /// </summary>
            public FloatHttpMethod Method { get; set; }

            /// <summary>
            /// The URL divided into sections.
            /// </summary>
            public string[] RouteSections { get; set; }

            /// <summary>
            /// Function to execute.
            /// </summary>
            public Func<FloatRouteContext, object> Function { get; set; }

            /// <summary>
            /// Middleware to execute.
            /// </summary>
            public List<Action<FloatRouteContext>> Middleware { get; set; }
        }

        /// <summary>
        /// A route cache entry.
        /// </summary>
        private class FloatRouteCacheEntry {
            /// <summary>
            /// URL for the route.
            /// </summary>
            public string RouteUrl { get; set; }

            /// <summary>
            /// HTTP method.
            /// </summary>
            public string Method { get; set; }

            /// <summary>
            /// Identifier for route entry.
            /// </summary>
            public string Guid { get; set; }

            /// <summary>
            /// Saved parameters.
            /// </summary>
            public Dictionary<string, string> Parameters { get; set; }
        }

        #endregion
    }

    /// <summary>
    /// A list of HTTP methods.
    /// </summary>
    public enum FloatHttpMethod {
        COPY,
        DELETE,
        GET,
        HEAD,
        LINK,
        LOCK,
        OPTIONS,
        PATCH,
        POST,
        PROPFIND,
        PURGE,
        PUT,
        UNLINK,
        UNLOCK,
        VIEW
    }

    /// <summary>
    /// Context passed to each function.
    /// </summary>
    public class FloatRouteContext {
        /// <summary>
        /// Whether or not the request is localhost.
        /// </summary>
        public bool IsLocal { get; set; }

        /// <summary>
        /// The request object.
        /// </summary>
        public HttpRequest Request { get; set; }

        /// <summary>
        /// A list of headers.
        /// </summary>
        public IHeaderDictionary Headers { get; set; }

        /// <summary>
        /// Route parameters.
        /// </summary>
        public Dictionary<string, string> Parameters { get; set; }

        /// <summary>
        /// Objects being passed between functions.
        /// </summary>
        public Dictionary<string, object> Objects { get; set; }

        /// <summary>
        /// Body of the request.
        /// </summary>
        public string Body { get; set; }

        /// <summary>
        /// Cast the body to a given class.
        /// </summary>
        public T BodyTo<T>() {
            try {
                return JsonConvert.DeserializeObject<T>(this.Body);
            }
            catch {
                return default(T);
            }
        }
    }

    /// <summary>
    /// Specific route exception.
    /// </summary>
    public class FloatRouteException : Exception {
        /// <summary>
        /// HTTP status code.
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// Error message.
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Payload to serve.
        /// </summary>
        public object Payload { get; set; }

        /// <summary>
        /// Constructor.
        /// </summary>
        public FloatRouteException(int statusCode, string errorMessage = null) {
            this.StatusCode = statusCode;
            this.ErrorMessage = errorMessage;
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        public FloatRouteException(int statusCode, object payload) {
            this.StatusCode = statusCode;
            this.Payload = payload;
        }
    }

    /// <summary>
    /// A response object.
    /// </summary>
    public class FloatRouteResponse {
        /// <summary>
        /// HTTP status code.
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// Headers to serve.
        /// </summary>
        public Dictionary<string, string> Headers { get; set; }

        /// <summary>
        /// Body of the response.
        /// </summary>
        public string Body { get; set; }

        /// <summary>
        /// Empty constructor.
        /// </summary>
        public FloatRouteResponse() {}

        /// <summary>
        /// Full constructor.
        /// </summary>
        public FloatRouteResponse(string body, int statusCode = 200, Dictionary<string, string> headers = null) {
            this.StatusCode = statusCode;
            this.Headers = headers;
            this.Body = body;
        }
    }

    /// <summary>
    /// Simple response object for HTML.
    /// </summary>
    public class FloatRouteHtmlResponse : FloatRouteResponse {
        /// <summary>
        /// Simple response object for HTML.
        /// </summary>
        public FloatRouteHtmlResponse(string html) {
            this.StatusCode = 200;
            this.Headers = new Dictionary<string, string> {{"Content-Type", "text/html"}};
            this.Body = html;
        }
    }

    /// <summary>
    /// Simple response object for JSON.
    /// </summary>
    public class FloatRouteJsonResponse : FloatRouteResponse {
        /// <summary>
        /// Simple response object for JSON.
        /// </summary>
        public FloatRouteJsonResponse(string json) {
            this.StatusCode = 200;
            this.Headers = new Dictionary<string, string> {{"Content-Type", "application/json"}};
            this.Body = json;
        }

        /// <summary>
        /// Simple response object for JSON.
        /// </summary>
        public FloatRouteJsonResponse(object payload) {
            this.StatusCode = 200;
            this.Headers = new Dictionary<string, string> {{"Content-Type", "application/json"}};
            this.Body = JsonConvert.SerializeObject(payload);
        }
    }
}