const folderTypeId = 'f38bd2d7-65d0-48e6-95dc-87ce06ec2d3d';

function imageFileValue(value) {
    if (typeof value !== 'string') return value;
    return value.trim().startsWith('{') ? JSON.parse(value) : {src:value};
}

/**
 * The backoffice media contract used by the diagram editor and preview.
 * Resolves the drawing and its two images and saves all three through native media properties.
 * Native API payloads, picker values and media-property parsing stay inside this module.
 */
class UmbracoMediaApi {
    constructor(host, services, diagramFolderId) {
        this.services = services;
        this.host = host;
        this.diagramFolderId = diagramFolderId;
    }
    get diagramTypeIds() { return ['0c4c6e74-02f5-43cb-92f5-36e3402a693b']; }
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
        for (const format of ['url', 'svgUrl']) {
            if (!preview[format] || !updated) continue;
            const url = new URL(preview[format], document.baseURI);
            url.searchParams.set('rnd', updated);
            preview[format] = url.href;
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
        const result = await this.services.MediaService.getMediaById({path:{id:this.diagramFolderId}});
        if (result.response?.status === 404 || result.response?.status === 403) return null;
        const folder = this.check(result, 'Umbraco could not read the diagram upload folder.');
        return !folder.isTrashed && folder.mediaType.id === folderTypeId ? folder.id : null;
    }
    diagramValues({png, source, svg, map}, previous = []) {
        const values = previous.slice();
        const set = (alias, editorAlias, value) => {
            const index = values.findIndex(property => property.alias === alias && !property.culture && !property.segment);
            const property = {alias, editorAlias, culture:null, segment:null, value};
            if (index < 0) values.push(property);
            else values[index] = property;
        };
        // Umbraco's upload field interprets a missing src on an existing file as a request to clear it.
        const file = (alias, editor, temporaryFileId) => {
            const current = previous.find(property => property.alias === alias);
            set(alias, editor, {...imageFileValue(current?.value), temporaryFileId});
        };
        file('umbracoFile', 'Umbraco.ImageCropper', png);
        file('drawioSource', 'Umbraco.UploadField', source);
        file('drawioSvg', 'Umbraco.UploadField', svg);
        set('drawioMap', 'Umbraco.TextArea', map);
        return values;
    }
    async createDiagram({id, files, parent, name}) {
        const folder = parent ?? await this.findDiagramFolder();
        this.check(await this.services.MediaService.postMedia({body:{id, parent:folder ? {id:folder} : null,
            mediaType:{id:this.diagramTypeIds[0]}, variants:[{name:name?.trim() || 'diagram', culture:null, segment:null}],
            values:this.diagramValues(files)}}), 'Umbraco could not create the diagram media.');
    }
    async updateDiagram(id, files) {
        const path = {id};
        const media = this.check(await this.services.MediaService.getMediaById({path}), 'Cannot read the selected media.');
        const variants = media.variants.map(({name, culture, segment}) => ({name, culture, segment}));
        this.check(await this.services.MediaService.putMediaById({path, body:{variants, values:this.diagramValues(files, media.values)}}),
            'Umbraco could not update the diagram media.');
    }
    normalize(media) {
        const url = alias => imageFileValue(media.values?.find(property => property.alias === alias)?.value)?.src;
        return {url:url('umbracoFile'), sourceUrl:url('drawioSource'), svgUrl:url('drawioSvg'),
            name:media.variants?.[0]?.name, parent:media.parent?.id};
    }
    check(result, message) {
        if (result?.error || !result?.response?.ok) throw Error(message);
        return result.data;
    }
}

export function createMediaApi(major, host, services, diagramFolderId) {
    if (major !== 17 && major !== 18) throw Error(`draw.io has no tested media adapter for Umbraco ${major}.`);
    return new UmbracoMediaApi(host, services, diagramFolderId);
}
