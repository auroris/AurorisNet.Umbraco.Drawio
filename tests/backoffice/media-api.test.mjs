import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';

// The browser module has no vendor imports: exercise the real adapters against the API contracts they consume.
const source = await readFile(new URL('../../src/wwwroot/App_Plugins/AurorisNet.Drawio/compatibility/umbraco-media-api.js', import.meta.url), 'utf8');
const {createMediaApi, Umbraco17MediaApi, Umbraco18MediaApi} = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);

const diagramFolderId = 'e32695fb-72a2-477f-8f64-fa80cba2fe14';

const files = {png:'png-upload', source:'source-upload', svg:'svg-upload', map:'{"links":[]}'};
const values = [
    ['umbracoFile', 'Umbraco.ImageCropper', {temporaryFileId:files.png}],
    ['drawioSource', 'Umbraco.UploadField', {temporaryFileId:files.source}],
    ['drawioSvg', 'Umbraco.UploadField', {temporaryFileId:files.svg}],
    ['drawioMap', 'Umbraco.TextArea', files.map]
].map(([alias, editorAlias, value]) => ({alias, editorAlias, culture:null, segment:null, value}));

function fixture(major) {
    const calls = [];
    const folder = {id:diagramFolderId, mediaType:{id:'f38bd2d7-65d0-48e6-95dc-87ce06ec2d3d'},
        variants:[{name:'Site plans'}], parent:{id:'another-folder'}, isTrashed:false};
    const media = {values:[{alias:'umbracoFile', value:{src:'/media/test/diagram.png'}},
        {alias:'drawioSource',value:{src:'/media/src/diagram.drawio'}},
        {alias:'drawioSvg',value:'/media/svg/diagram.svg'}], variants:[{name:'Plan'}], parent:{id:'folder'}};
    const services = {
        MediaService:{
            getMediaById:async value => {
                calls.push(['read',value]);
                return {response:{ok:true},data:value.path.id === diagramFolderId ? folder : media};
            },
            postMedia:async value => { calls.push(['create',value]); return {response:{ok:true}}; },
            putMediaById:async value => { calls.push(['update',value]); return {response:{ok:true}}; }
        },
        TemporaryFileService:{postTemporaryFile:async value => { calls.push(['upload',value]); return {response:{ok:true}}; }},
        MediaDetailRepository:class {
            constructor(host) { calls.push(['previewHost',host]); }
            async requestByUnique(key) { calls.push(['preview',key]); return {data:media}; }
        }
    };
    return {api:createMediaApi(major,'host',services,diagramFolderId), calls, media, services, folder};
}

for (const [major, type] of [[17,Umbraco17MediaApi],[18,Umbraco18MediaApi]]) {
    test(`Umbraco ${major}: reads, previews and normalizes original media files`, async () => {
        const {api,calls,media} = fixture(major);
        assert.ok(api instanceof type);
        for (const value of [{src:'/media/test/diagram.png'}, '{"src":"/media/test/diagram.png"}', '/media/test/diagram.png']) {
            media.values[0].value = value;
            assert.deepEqual(await api.readMedia('image'), {url:'/media/test/diagram.png',sourceUrl:'/media/src/diagram.drawio',svgUrl:'/media/svg/diagram.svg',name:'Plan',parent:'folder'});
            assert.equal((await api.readPreview('image')).url, '/media/test/diagram.png');
        }
        assert.deepEqual(calls[0], ['read',{path:{id:'image'}}]);
        assert.equal(calls.filter(([method]) => method === 'previewHost').length, 1);
        media.values = [];
        assert.equal((await api.readMedia('image')).url, undefined);
    });

    test(`Umbraco ${major}: upload and create use the authenticated media API contract`, async () => {
        const {api,calls} = fixture(major);
        const file = new Blob(['diagram'],{type:'image/png'});
        const temporaryFileId = await api.upload(file);
        assert.deepEqual(calls[0], ['upload',{body:{Id:temporaryFileId,File:file}}]);
        await api.createDiagram({id:'image',files,parent:'folder',name:'Plan'});
        assert.deepEqual(calls[1], ['create',{body:{id:'image',parent:{id:'folder'},mediaType:{id:api.diagramTypeIds[0]},
            variants:[{name:'Plan',culture:null,segment:null}],
            values}}]);
        await api.createDiagram({id:'new-image',files,name:'About us'});
        assert.deepEqual(calls[2], ['read',{path:{id:diagramFolderId}}]);
        assert.deepEqual(calls[3][1].body.parent,{id:diagramFolderId});
        assert.equal(calls.at(-1)[1].body.variants[0].name,'About us');
        const previous = [{key:'picker-key',mediaKey:'old-image'}];
        assert.deepEqual(api.pickMedia('image',previous),[{key:'picker-key',mediaKey:'image'}]);
        assert.equal(api.mediaKey(api.pickMedia('image')), 'image');
        assert.equal(api.mediaKey({mediaKey:'image'}), 'image');
    });

    test(`Umbraco ${major}: one edit replaces all files, clears obsolete links and retains other media properties`, async () => {
        const {api,media,calls} = fixture(major);
        const cropper = {src:'/media/test/plan.png', focalPoint:{left:0.2,top:0.8}, crops:[]};
        media.values = [{alias:'umbracoFile',editorAlias:'Umbraco.ImageCropper',value:JSON.stringify(cropper)},
            {alias:'altText',culture:'en-US',value:'A plan'}, ...media.values.slice(1)];
        media.variants = [{name:'Plan (1)',culture:'en-US',segment:null,updateDate:'2026-01-02'}];
        await api.updateDiagram('image',{...files, map:null});
        assert.deepEqual(calls,[['read',{path:{id:'image'}}],['update',{path:{id:'image'},body:{
            variants:[{name:'Plan (1)',culture:'en-US',segment:null}],
            values:[{...values[0],value:{...cropper,temporaryFileId:files.png}}, media.values[1],
                {...values[1], value:{src:'/media/src/diagram.drawio',temporaryFileId:files.source}},
                {...values[2], value:{src:'/media/svg/diagram.svg',temporaryFileId:files.svg}}, {...values[3], value:null}]
        }}]]);
    });

    test(`Umbraco ${major}: media failures are reported to the editor`, async () => {
        const {api,services} = fixture(major);
        services.MediaService.putMediaById = async () => ({response:{ok:false}});
        await assert.rejects(api.updateDiagram('image',files), /could not update/);
        services.MediaService.getMediaById = async () => ({response:{ok:false,status:403}});
        services.MediaService.postMedia = async () => ({response:{ok:false}});
        services.TemporaryFileService.postTemporaryFile = async () => ({error:{status:413}});
        await assert.rejects(api.readMedia('image'), /Cannot read/);
        await assert.rejects(api.upload(new Blob()), /could not upload/);
        await assert.rejects(api.createDiagram({id:'image',files}), /could not create the diagram media/);
        services.MediaService.getMediaById = async () => ({response:{ok:false,status:500}});
        await assert.rejects(api.createDiagram({id:'image',files}), /could not read the diagram upload folder/);
    });
}

for (const state of ['missing','forbidden','trashed','wrong media type']) {
    test(`a ${state} upload folder sends the new diagram to the root`, async () => {
        const {api, folder, services, calls} = fixture(18);
        if (state === 'missing' || state === 'forbidden') {
            services.MediaService.getMediaById = async ({path}) => {
                assert.equal(path.id, diagramFolderId);
                return {response:{ok:false,status:state === 'missing' ? 404 : 403}};
            };
        } else if (state === 'trashed') folder.isTrashed = true;
        else folder.mediaType.id = 'file-type';
        await api.createDiagram({id:'new-image',files,name:'   '});
        const image = calls.at(-1)[1].body;
        assert.equal(image.parent,null);
        assert.equal(image.mediaType.id,api.diagramTypeIds[0]);
        assert.equal(image.variants[0].name,'diagram');
    });
}

test('unsupported CMS versions never silently select another adapter', () => {
    for (const major of [16,19,undefined,'17']) assert.throws(() => createMediaApi(major,null,{}), /no tested media adapter/);
});
