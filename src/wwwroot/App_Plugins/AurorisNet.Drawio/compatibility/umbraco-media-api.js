/**
 * The backoffice media contract used by the diagram editor and preview.
 * Reads return {url, name, parent}; writes accept package-owned IDs and browser Files.
 * Native API payloads, picker values and media-property parsing stay inside this module.
 */
class UmbracoMediaApi {
    constructor(host, services) {
        this.services = services;
        this.host = host;
    }
    get imageTypeIds() { return ['cc07b313-0843-4aa8-bbda-871c8da728c8']; }
    mediaKey(value) { return Array.isArray(value) ? value[0]?.mediaKey : value?.mediaKey; }
    pickMedia(id, previous) { return [{key:previous?.[0]?.key || crypto.randomUUID(), mediaKey:id}]; }
    async readMedia(id) {
        return this.normalize(this.check(await this.services.MediaService.getMediaById({path:{id}}), 'Cannot read the selected media.'));
    }
    async readPreview(id) {
        this.previewRepository ??= new this.services.MediaDetailRepository(this.host);
        const {data, error} = await this.previewRepository.requestByUnique(id);
        if (error || !data) throw Error('The diagram is unavailable.');
        return this.normalize(data);
    }
    async upload(file) {
        const id = crypto.randomUUID();
        this.check(await this.services.TemporaryFileService.postTemporaryFile({body:{Id:id, File:file}}),
            'Umbraco could not upload this diagram. Check its size and your media permissions.');
        return id;
    }
    async createImage({id, temporaryFileId, parent, name}) {
        this.check(await this.services.MediaService.postMedia({body:{id, parent:parent ? {id:parent} : null,
            mediaType:{id:this.imageTypeIds[0]}, variants:[{name:name || 'Diagram', culture:null, segment:null}],
            values:[{alias:'umbracoFile', editorAlias:'Umbraco.ImageCropper', culture:null, segment:null, value:{temporaryFileId}}]}}),
            'Umbraco could not create the diagram media. Your original is unchanged.');
    }
    normalize(media) {
        let file = media.values?.find(value => value.alias === 'umbracoFile')?.value;
        if (typeof file === 'string' && file.trim().startsWith('{')) file = JSON.parse(file);
        return {url:typeof file === 'string' ? file : file?.src, name:media.variants?.[0]?.name, parent:media.parent?.id};
    }
    check(result, message) {
        if (result?.error || !result?.response?.ok) throw Error(message);
        return result.data;
    }
}

// These versions share the media calls we use. Version-specific changes belong in their respective adapters.
export class Umbraco17MediaApi extends UmbracoMediaApi {}
export class Umbraco18MediaApi extends UmbracoMediaApi {}

export function createMediaApi(major, host, services) {
    switch (major) {
        case 17: return new Umbraco17MediaApi(host, services);
        case 18: return new Umbraco18MediaApi(host, services);
        default: throw Error(`draw.io has no tested media adapter for Umbraco ${major}.`);
    }
}
