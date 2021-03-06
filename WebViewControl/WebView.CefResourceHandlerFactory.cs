﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using CefSharp;

namespace WebViewControl {

    partial class WebView {

        private Dictionary<string, Assembly> assemblies;
        private bool newAssembliesLoaded = true;

        private class CefResourceHandlerFactory : IResourceHandlerFactory {

            private readonly WebView OwnerWebView;

            public CefResourceHandlerFactory(WebView webView) {
                OwnerWebView = webView;
            }

            public bool HasHandlers {
                get { return true; }
            }

            IResourceHandler IResourceHandlerFactory.GetResourceHandler(IWebBrowser browserControl, IBrowser browser, IFrame frame, IRequest request) {
                if (request.Url == OwnerWebView.DefaultLocalUrl) {
                    return OwnerWebView.htmlToLoad != null ? CefSharp.ResourceHandler.FromString(OwnerWebView.htmlToLoad, Encoding.UTF8) : null;
                }

                if (OwnerWebView.FilterRequest(request)) {
                    return null;
                }

                Uri url;
                var resourceHandler = new ResourceHandler(request, OwnerWebView.GetRequestUrl(request));
                if (Uri.TryCreate(resourceHandler.Url, UriKind.Absolute, out url) && url.Scheme == ResourceUrl.EmbeddedScheme) {
                    var urlWithoutQuery = new UriBuilder(url);
                    if (url.Query != "") {
                        urlWithoutQuery.Query = "";
                    }
                    OwnerWebView.ExecuteWithAsyncErrorHandling(() => OwnerWebView.LoadEmbeddedResource(resourceHandler, urlWithoutQuery.Uri));
                }

                if (OwnerWebView.BeforeResourceLoad != null) {
                    OwnerWebView.ExecuteWithAsyncErrorHandling(() => OwnerWebView.BeforeResourceLoad(resourceHandler));
                }

                if (resourceHandler.Handled) {
                    return resourceHandler.Handler;
                } else if (!OwnerWebView.IgnoreMissingResources && url != null && url.Scheme == ResourceUrl.EmbeddedScheme) {
                    if (OwnerWebView.ResourceLoadFailed != null) {
                        OwnerWebView.ResourceLoadFailed(request.Url);
                    } else {
                        OwnerWebView.ExecuteWithAsyncErrorHandling(() => throw new InvalidOperationException("Resource not found: " + request.Url));
                    }
                }

                return null;
            }
        }

        protected virtual string GetRequestUrl(IRequest request) {
            return request.Url;
        }

        protected virtual void LoadEmbeddedResource(ResourceHandler resourceHandler, Uri url) {
            var resourceAssembly = ResolveResourceAssembly(url);
            var resourcePath = ResourceUrl.GetEmbeddedResourcePath(url);

            var extension = Path.GetExtension(resourcePath.Last()).ToLower();

            var resourceStream = TryGetResourceWithFullPath(resourceAssembly, resourcePath);
            if (resourceStream != null) {
                resourceHandler.RespondWith(resourceStream, extension);
            }
        }

        protected Assembly ResolveResourceAssembly(Uri resourceUrl) {
            if (assemblies == null) {
                assemblies = new Dictionary<string, Assembly>();
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoaded;
            }

            var assemblyName = ResourceUrl.GetEmbeddedResourceAssemblyName(resourceUrl);
            var assembly = GetAssemblyByName(assemblyName);

            if (assembly == null) {
                if (newAssembliesLoaded) {
                    // add loaded assemblies to cache
                    newAssembliesLoaded = false;
                    foreach (var domainAssembly in AppDomain.CurrentDomain.GetAssemblies()) {
                        // replace if duplicated (can happen)
                        assemblies[domainAssembly.GetName().Name] = domainAssembly;
                    }
                }

                assembly = GetAssemblyByName(assemblyName);
                if (assembly == null) {
                    // try load assembly from its name
                    assembly = AppDomain.CurrentDomain.Load(new AssemblyName(assemblyName));
                    if (assembly != null) {
                        assemblies[assembly.GetName().Name] = assembly;
                    }
                }
            }

            if (assembly != null) {
                return assembly;
            }

            throw new InvalidOperationException("Could not find assembly for: " + resourceUrl);
        }

        private Assembly GetAssemblyByName(string assemblyName) {
            Assembly assembly;
            assemblies.TryGetValue(assemblyName, out assembly);
            return assembly;
        }

        protected virtual Stream TryGetResourceWithFullPath(Assembly assembly, IEnumerable<string> resourcePath) {
            return ResourcesManager.TryGetResourceWithFullPath(assembly, resourcePath);
        }

        private void OnAssemblyLoaded(object sender, AssemblyLoadEventArgs args) {
            newAssembliesLoaded = true;
        }
    }
}
