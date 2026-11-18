using Drawio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AurorisNet.Umbraco.Drawio.Tests;

/// <summary>The Drawio settings section: binding, defaults and the checks at startup.</summary>
public sealed class SettingsTests
{
    [Fact]
    public void Defaults_are_valid_and_safe()
    {
        var options = new DrawioOptions();
        Assert.True(new DrawioOptionsValidation().Validate(null, options).Succeeded);
        Assert.True(options.RedirectMediaFiles);
        Assert.Equal(SupersededImageHandling.Delete, options.SupersededImages);
        Assert.False(options.IsSelfHosted);
        Assert.True(new DrawioOptions { Bundled = true }.IsSelfHosted);
        Assert.True(new DrawioOptions { WebRoot = "draw" }.IsSelfHosted);
        Assert.Empty(options.Libraries);
        Assert.Empty(options.Plugins);
    }

    [Fact]
    public void The_section_binds_from_configuration()
    {
        var options = Bind(new()
        {
            ["Drawio:WebRoot"] = @"C:\inetpub\drawio",
            ["Drawio:RedirectMediaFiles"] = "false",
            ["Drawio:SupersededImages"] = "Delete",
            ["Drawio:DefaultLibraries"] = "general;uml",
            ["Drawio:LibraryGroup"] = "Extra icons",
            ["Drawio:Libraries:0:Id"] = "site-icons",
            ["Drawio:Libraries:0:Title"] = "Site icons",
            ["Drawio:Libraries:0:Description"] = "CC BY 4.0 - Example",
            ["Drawio:Libraries:0:Url"] = "/App_Plugins/MySite/icons/site-icons.xml",
            ["Drawio:Plugins:0:Name"] = "notes",
            ["Drawio:Plugins:0:Url"] = "/App_Plugins/MySite/notes/notes.js",
        });
        Assert.Equal(@"C:\inetpub\drawio", options.WebRoot);
        Assert.False(options.RedirectMediaFiles);
        Assert.Equal(SupersededImageHandling.Delete, options.SupersededImages);
        Assert.Equal("general;uml", options.DefaultLibraries);
        Assert.Equal("Extra icons", options.LibraryGroup);
        Assert.Equal("site-icons", Assert.Single(options.Libraries).Id);
        Assert.Equal("/App_Plugins/MySite/notes/notes.js", Assert.Single(options.Plugins).Url);
    }

    [Fact]
    public void A_relative_web_root_is_under_the_content_root()
    {
        var environment = new TestEnvironment { ContentRootPath = Path.GetTempPath() };
        Assert.Equal(Path.Combine(Path.GetTempPath(), "draw"), new DrawioOptions { WebRoot = "draw" }.DirectoryPath(environment));
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "DrawioDistribution"), new DrawioOptions { WebRoot = " " }.DirectoryPath(environment));
    }

    [Theory]
    [InlineData("Drawio:Plugins:0:Url", "https://cdn.example.com/p.js", "Drawio:Plugins:0:Url must be a path on this site")]
    [InlineData("Drawio:Plugins:0:Url", "//cdn.example.com/p.js", "Drawio:Plugins:0:Url must be a path on this site")]
    [InlineData("Drawio:Plugins:0:Name", "two words", "Drawio:Plugins:0:Name must be letters")]
    [InlineData("Drawio:Libraries:0:Url", "shapes.xml", "Drawio:Libraries:0:Url must be a path on this site")]
    [InlineData("Drawio:Libraries:0:Id", "a;b", "Drawio:Libraries:0:Id must be letters")]
    [InlineData("Drawio:Libraries:0:Title", " ", "Drawio:Libraries:0:Title must not be empty")]
    [InlineData("Drawio:DefaultLibraries", "general;;uml", "Drawio:DefaultLibraries must be library names")]
    [InlineData("Drawio:DefaultLibraries", "general;'x'", "Drawio:DefaultLibraries must be library names")]
    [InlineData("Drawio:LibraryGroup", "", "Drawio:LibraryGroup must not be empty")]
    public void Invalid_settings_stop_the_site_with_a_reason(string key, string value, string message)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Drawio:Plugins:0:Name"] = "plugin",
            ["Drawio:Plugins:0:Url"] = "/p.js",
            ["Drawio:Libraries:0:Id"] = "lib",
            ["Drawio:Libraries:0:Title"] = "Library",
            ["Drawio:Libraries:0:Url"] = "/l.xml",
            [key] = value,
        };
        var error = Assert.Throws<OptionsValidationException>(() => Bind(settings));
        Assert.Contains(error.Failures, failure => failure.StartsWith(message, StringComparison.Ordinal));
    }

    /// <summary>A settings file that spells out the empty default must not stop the site.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void An_empty_default_library_list_keeps_draw_io_own_set(string value)
    {
        var options = Bind(new() { ["Drawio:DefaultLibraries"] = value });
        Assert.Equal("null", DrawioScripts.Configuration(new DrawioScriptContext("", options)));
    }

    [Fact]
    public void Duplicate_names_are_refused()
    {
        var options = new DrawioOptions
        {
            Libraries = [new() { Id = "a", Title = "A", Url = "/a.xml" }, new() { Id = "a", Title = "B", Url = "/b.xml" }],
            Plugins = [new() { Name = "p", Url = "/a.js" }, new() { Name = "p", Url = "/b.js" }],
        };
        var result = new DrawioOptionsValidation().Validate(null, options);
        Assert.Contains("Drawio:Libraries names 'a' more than once.", result.Failures!);
        Assert.Contains("Drawio:Plugins names 'p' more than once.", result.Failures!);
    }

    [Fact]
    public void An_unknown_handling_is_refused() => Assert.Contains("Drawio:SupersededImages must be RecycleBin, Delete or Keep.",
        new DrawioOptionsValidation().Validate(null, new DrawioOptions { SupersededImages = (SupersededImageHandling)7 }).Failures!);

    private static DrawioOptions Bind(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddOptions<DrawioOptions>().Bind(configuration.GetSection(DrawioOptions.Section));
        services.AddSingleton<IValidateOptions<DrawioOptions>, DrawioOptionsValidation>();
        return services.BuildServiceProvider().GetRequiredService<IOptions<DrawioOptions>>().Value;
    }

    private sealed class TestEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
