using System.Text;
using System.Text.Json;
using Drawio;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.ContentEditing;
using Umbraco.Cms.Core.Services;

namespace AurorisNet.Umbraco.Drawio.Tests;

public sealed partial class SiteTests
{
    [Fact]
    public async Task Umbracos_media_editor_creates_and_replaces_three_files_on_one_item()
    {
        var temporary = Site.Services.GetRequiredService<ITemporaryFileService>();
        var editing = Site.Services.GetRequiredService<IMediaEditingService>();
        var id = Guid.NewGuid();
        async Task<PropertyValueModel> Upload(string alias, string extension, byte[] data)
        {
            var key = Guid.NewGuid();
            var upload = await temporary.CreateAsync(new() { FileName = $"diagram-{id:N}.{extension}", Key = key, OpenReadStream = () => new MemoryStream(data) });
            Assert.True(upload.Success, upload.Status.ToString());
            var existing = await editing.GetAsync(id);
            var src = existing?.GetValue<string>(alias);
            if (existing is not null && alias == "umbracoFile") src = "/media/" + ImagePath(existing);
            return new() { Alias = alias, Value = JsonSerializer.Serialize(new { src, temporaryFileId = key }) };
        }
        const string svg = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><a href="/room"><rect width="10" height="10"/></a></svg>""";
        for (var revision = 1; revision <= 2; revision++)
        {
            var source = $"<mxfile><diagram name='Revision {revision}'/></mxfile>";
            PropertyValueModel[] properties = [
                await Upload("umbracoFile", "png", Png),
                await Upload("drawioSource", "drawio", Encoding.UTF8.GetBytes(source)),
                await Upload("drawioSvg", "svg", Encoding.UTF8.GetBytes(svg)),
                new() { Alias = "drawioMap", Value = revision == 1 ? "{}" : null }
            ];
            VariantModel[] variants = [new() { Name = "Building" }];
            if (revision == 1)
            {
                var result = await editing.CreateAsync(new() { Key = id, ParentKey = DiagramMediaFolder.Key,
                    ContentTypeKey = DrawioInstallation.MediaTypeKey, Properties = properties, Variants = variants }, Constants.Security.SuperUserKey);
                Assert.True(result.Success, result.Status.ToString());
            }
            else
            {
                var result = await editing.UpdateAsync(id, new() { Properties = properties, Variants = variants }, Constants.Security.SuperUserKey);
                Assert.True(result.Success, result.Status.ToString());
            }
            var saved = await editing.GetAsync(id);
            Assert.NotNull(saved);
            Assert.Equal(Png, await Http.GetByteArrayAsync("media/" + ImagePath(saved), Cancel));
            using var request = new HttpRequestMessage(HttpMethod.Get, saved.GetValue<string>("drawioSource"));
            request.Headers.Accept.ParseAdd("application/xml");
            using var response = await Http.SendAsync(request, Cancel);
            Assert.Equal(source, await response.Content.ReadAsStringAsync(Cancel));
            Assert.Equal(svg, await Http.GetStringAsync(saved.GetValue<string>("drawioSvg"), Cancel));
            Assert.Equal(revision == 1 ? "{}" : null, saved.GetValue<string>("drawioMap"));
        }
    }
}
