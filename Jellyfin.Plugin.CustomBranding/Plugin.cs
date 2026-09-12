using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CustomBranding
{
    // Configuration sauvegardée
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string Favicon { get; set; } = string.Empty;
        public string IconTransparent { get; set; } = string.Empty;
        public string BannerLight { get; set; } = string.Empty;
        public string BannerDark { get; set; } = string.Empty;
    }

    // Déclaration du Plugin et IHasWebPages pour l'interface d'administration
    public class CustomBrandingPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public static CustomBrandingPlugin? Instance { get; private set; }
        public override string Name => "Custom Branding (Lightweight)";
        public override Guid Id => Guid.Parse("A1B2C3D4-E5F6-4789-8901-23456789ABCD");
        public override string Description => "Remplace les logos, bannières et favicons nativement. Repose sur le plugin File Transformation.";

        public CustomBrandingPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = this.Name,
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
                }
            };
        }
    }

    // Le contrôleur qui sert le JS dynamique contenant la configuration de l'utilisateur
    [ApiController]
    [Route("CustomBranding")]
    public class CustomBrandingController : ControllerBase
    {
        [HttpGet("branding.js")]
        public ActionResult GetScript()
        {
            var config = CustomBrandingPlugin.Instance?.Configuration;
            if (config == null)
            {
                return NotFound();
            }

            // Génération du JS qui applique les icônes Apple et injecte le CSS
            var script = $@"
(function() {{
    'use strict';

    // 1. Remplacement strict de toutes les balises Favicon et Apple Touch
    const faviconUrl = '{config.Favicon}';
    if (faviconUrl) {{
        const selectors = [
            'link[rel=""icon""]',
            'link[rel=""shortcut icon""]',
            'link[rel=""apple-touch-icon""]',
            'link[rel=""mask-icon""]'
        ];
        selectors.forEach(selector => {{
            document.querySelectorAll(selector).forEach(el => el.href = faviconUrl);
        }});
    }}

    // 2. Injection du CSS natif (Méthode Jellyfin Enhanced)
    // Cela remplace l'image des balises <img> sans avoir besoin d'écouter les clics (MutationObserver)
    const css = `
        /* Logo Transparent (Barre Supérieure & Menu Admin) */
        {(string.IsNullOrEmpty(config.IconTransparent) ? "" : $@".pageTitleWithLogo {{ background-image: url('{config.IconTransparent}') !important; }}
        .adminDrawerLogo img {{ content: url('{config.IconTransparent}') !important; }}")}

        /* Bannières Écran de Connexion (Dark / Light) */
        {(string.IsNullOrEmpty(config.BannerDark) ? "" : $@".theme-dark .splashLogo, .theme-dark .formDialogHeaderLogo, html.theme-dark body .splashLogo {{ content: url('{config.BannerDark}') !important; }}")}
        {(string.IsNullOrEmpty(config.BannerLight) ? "" : $@".theme-light .splashLogo, .theme-light .formDialogHeaderLogo, html:not(.theme-dark) body:not(.theme-dark) .splashLogo {{ content: url('{config.BannerLight}') !important; }}")}
    `;

    const style = document.createElement('style');
    style.id = 'custom-branding-style';
    style.textContent = css;
    document.head.appendChild(style);
}})();";

            // Désactive le cache pour que le changement de logo soit immédiat à la sauvegarde
            Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            return Content(script, "application/javascript");
        }
    }

    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddHostedService<BrandingInjector>();
        }
    }

    // Le moteur d'injection via le plugin File Transformation
    public class BrandingInjector : IHostedService
    {
        private readonly ILogger<BrandingInjector> _logger;

        public BrandingInjector(ILogger<BrandingInjector> logger)
        {
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Recherche du plugin File Transformation par réflexion
                var ftAssembly = AssemblyLoadContext.Default.Assemblies
                    .FirstOrDefault(x => x.FullName?.Contains("Jellyfin.Plugin.FileTransformation") ?? false);

                if (ftAssembly != null)
                {
                    var ftInterface = ftAssembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");
                    var jObjectType = ftAssembly.GetType("Newtonsoft.Json.Linq.JObject")
                                   ?? AssemblyLoadContext.Default.Assemblies
                                        .FirstOrDefault(x => x.FullName?.Contains("Newtonsoft.Json") ?? false)
                                        ?.GetType("Newtonsoft.Json.Linq.JObject");

                    if (ftInterface != null && jObjectType != null)
                    {
                        // Payload EXACT de Jellyfin Enhanced pour enregistrer la transformation dans index.html
                        string jsonPayload = $@"{{
                            ""id"": ""{CustomBrandingPlugin.Instance!.Id}-branding"",
                            ""name"": ""Custom Branding Injector"",
                            ""pattern"": ""index.html"",
                            ""search"": ""</head>"",
                            ""replacement"": ""<script src=\\""/CustomBranding/branding.js\\"" type=\\""text/javascript\\""></script></head>"",
                            ""regex"": false
                        }}";

                        var parseMethod = jObjectType.GetMethod("Parse", new[] { typeof(string) });
                        var jObjectPayload = parseMethod?.Invoke(null, new object[] { jsonPayload });

                        ftInterface.GetMethod("RegisterTransformation")?.Invoke(null, new object[] { jObjectPayload! });
                        _logger.LogInformation("Custom Branding : Enregistré avec succès via le plugin File Transformation !");
                    }
                }
                else
                {
                    _logger.LogWarning("Custom Branding : Le plugin File Transformation est introuvable. Veuillez l'installer.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Custom Branding : Erreur lors de l'enregistrement de l'injection.");
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
