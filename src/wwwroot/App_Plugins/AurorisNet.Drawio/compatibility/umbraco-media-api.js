const folderTypeId = 'f38bd2d7-65d0-48e6-95dc-87ce06ec2d3d';
const folderName = 'drawio';

function imageFileValue(value) {
    if (typeof value !== 'string') return value;
    return value.trim().startsWith('{') ? JSON.parse(value) : {src:value};
}

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
        const preview = this.normalize(data);
        const updated = data.variants?.[0]?.updateDate;
        if (preview.url && updated) {
            const url = new URL(preview.url, document.baseURI);
            url.searchParams.set('rnd', updated);
            preview.url = url.href;
        }
        return preview;
    }
    async upload(file) {
        const id = crypto.randomUUID();
        this.check(await this.services.TemporaryFileService.postTemporaryFile({body:{Id:id, File:file}}),
            'Umbraco could not upload this diagram. Check its size and your media permissions.');
        return id;
    }
    async findDiagramFolder() {
        let skip = 0;
        while (true) {
            const page = this.check(await this.services.MediaService.getTreeMediaRoot({query:{skip, take:100}}),
                'Umbraco could not read the media library to find the drawio folder.');
            for (const item of page.items) {
                if (!item.parent && !item.isTrashed && item.mediaType.id === folderTypeId
                    && item.variants[0]?.name?.toLowerCase() === folderName) return item.id;
            }
            skip += page.items.length;
            if (skip >= page.total || !page.items.length) break;
        }
        return null;
    }
    async createImage({id, temporaryFileId, parent, name}) {
        const folder = parent ?? await this.findDiagramFolder();
        this.check(await this.services.MediaService.postMedia({body:{id, parent:folder ? {id:folder} : null,
            mediaType:{id:this.imageTypeIds[0]}, variants:[{name:name?.trim() || 'diagram', culture:null, segment:null}],
            values:[{alias:'umbracoFile', editorAlias:'Umbraco.ImageCropper', culture:null, segment:null, value:{temporaryFileId}}]}}),
            'Umbraco could not create the diagram media. Your original is unchanged.');
    }
    async updateImage(id, temporaryFileId) {
        const path = {id};
        const media = this.check(await this.services.MediaService.getMediaById({path}), 'Cannot read the selected media.');
        const file = media.values.find(property => property.alias === 'umbracoFile');
        if (!file) throw Error('The selected media has no file property.');
        const values = media.values.map(property => property === file
            ? {...file, value:{...imageFileValue(file.value), temporaryFileId}} : property);
        const variants = media.variants.map(({name, culture, segment}) => ({name, culture, segment}));
        this.check(await this.services.MediaService.putMediaById({path, body:{variants, values}}),
            'Umbraco could not update the diagram media.');
    }
    normalize(media) {
        const file = media.values?.find(value => value.alias === 'umbracoFile');
        return {url:imageFileValue(file?.value)?.src, name:media.variants?.[0]?.name, parent:media.parent?.id};
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
