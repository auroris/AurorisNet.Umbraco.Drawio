import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';

// The browser module has no vendor imports: exercise the real adapters against the API contracts they consume.
const source = await readFile(new URL('../../src/wwwroot/App_Plugins/AurorisNet.Drawio/compatibility/umbraco-media-api.js', import.meta.url), 'utf8');
const {createMediaApi, Umbraco17MediaApi, Umbraco18MediaApi} = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);

function fixture(major) {
    const calls = [];
    const media = {values:[{alias:'umbracoFile', value:{src:'/media/test/diagram.png'}}], variants:[{name:'Plan'}], parent:{id:'folder'}};
    const services = {
        MediaService:{
            getMediaById:async value => { calls.push(['read',value]); return {response:{ok:true},data:media}; },
            postMedia:async value => { calls.push(['create',value]); return {response:{ok:true}}; }
        },
        TemporaryFileService:{postTemporaryFile:async value => { calls.push(['upload',value]); return {response:{ok:true}}; }},
        MediaDetailRepository:class {
            constructor(host) { calls.push(['previewHost',host]); }
            async requestByUnique(key) { calls.push(['preview',key]); return {data:media}; }
        }
    };
    return {api:createMediaApi(major,'host',services), calls, media, services};
}

for (const [major, type] of [[17,Umbraco17MediaApi],[18,Umbraco18MediaApi]]) {
    test(`Umbraco ${major}: reads, previews and normalizes original media files`, async () => {
        const {api,calls,media} = fixture(major);
        assert.ok(api instanceof type);
        for (const value of [{src:'/media/test/diagram.png'}, '{"src":"/media/test/diagram.png"}', '/media/test/diagram.png']) {
            media.values[0].value = value;
            assert.deepEqual(await api.readMedia('image'), {url:'/media/test/diagram.png',name:'Plan',parent:'folder'});
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
        await api.createImage({id:'image',temporaryFileId,parent:'folder',name:'Plan'});
        assert.deepEqual(calls[1], ['create',{body:{id:'image',parent:{id:'folder'},mediaType:{id:api.imageTypeIds[0]},
            variants:[{name:'Plan',culture:null,segment:null}],
            values:[{alias:'umbracoFile',editorAlias:'Umbraco.ImageCropper',culture:null,segment:null,value:{temporaryFileId}}]}}]);
        await api.createImage({id:'new-image',temporaryFileId});
        assert.equal(calls[2][1].body.parent,null);
        assert.equal(calls[2][1].body.variants[0].name,'Diagram');
        const previous = [{key:'picker-key',mediaKey:'old-image'}];
        assert.deepEqual(api.pickMedia('image',previous),[{key:'picker-key',mediaKey:'image'}]);
        assert.equal(api.mediaKey(api.pickMedia('image')), 'image');
        assert.equal(api.mediaKey({mediaKey:'image'}), 'image');
    });

    test(`Umbraco ${major}: media failures are reported to the editor`, async () => {
        const {api,services} = fixture(major);
        services.MediaService.getMediaById = async () => ({response:{ok:false},error:{status:403}});
        services.MediaService.postMedia = async () => ({response:{ok:false}});
        services.TemporaryFileService.postTemporaryFile = async () => ({error:{status:413}});
        await assert.rejects(api.readMedia('image'), /Cannot read/);
        await assert.rejects(api.upload(new Blob()), /could not upload/);
        await assert.rejects(api.createImage({id:'image',temporaryFileId:'temp'}), /could not create/);
    });
}

test('unsupported CMS versions never silently select another adapter', () => {
    for (const major of [16,19,undefined,'17']) assert.throws(() => createMediaApi(major,null,{}), /no tested media adapter/);
});
