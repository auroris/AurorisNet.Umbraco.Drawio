import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';

// The browser module has no vendor imports: exercise the real adapters against the API contracts they consume.
const source = await readFile(new URL('../../src/wwwroot/App_Plugins/AurorisNet.Drawio/compatibility/umbraco-media-api.js', import.meta.url), 'utf8');
const {createMediaApi, Umbraco17MediaApi, Umbraco18MediaApi} = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);

function fixture(major) {
    const calls = [];
    const root = [];
    const media = {values:[{alias:'umbracoFile', value:{src:'/media/test/diagram.png'}}], variants:[{name:'Plan'}], parent:{id:'folder'}};
    const services = {
        MediaService:{
            getMediaById:async value => { calls.push(['read',value]); return {response:{ok:true},data:media}; },
            getTreeMediaRoot:async ({query:{skip,take}}) => {
                calls.push(['root',skip]);
                return {response:{ok:true},data:{items:root.slice(skip,skip+take),total:root.length}};
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
    return {api:createMediaApi(major,'host',services), calls, media, services, root};
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
        const {api,calls,root} = fixture(major);
        const file = new Blob(['diagram'],{type:'image/png'});
        const temporaryFileId = await api.upload(file);
        assert.deepEqual(calls[0], ['upload',{body:{Id:temporaryFileId,File:file}}]);
        await api.createImage({id:'image',temporaryFileId,parent:'folder',name:'Plan'});
        assert.deepEqual(calls[1], ['create',{body:{id:'image',parent:{id:'folder'},mediaType:{id:api.imageTypeIds[0]},
            variants:[{name:'Plan',culture:null,segment:null}],
            values:[{alias:'umbracoFile',editorAlias:'Umbraco.ImageCropper',culture:null,segment:null,value:{temporaryFileId}}]}}]);
        root.push({id:'existing',mediaType:{id:'f38bd2d7-65d0-48e6-95dc-87ce06ec2d3d'},variants:[{name:'drawio'}]});
        await api.createImage({id:'new-image',temporaryFileId,name:'About us'});
        assert.deepEqual(calls.slice(2).map(([action]) => action),['root','create']);
        assert.deepEqual(calls.at(-1)[1].body.parent,{id:'existing'});
        assert.equal(calls.at(-1)[1].body.variants[0].name,'About us');
        const previous = [{key:'picker-key',mediaKey:'old-image'}];
        assert.deepEqual(api.pickMedia('image',previous),[{key:'picker-key',mediaKey:'image'}]);
        assert.equal(api.mediaKey(api.pickMedia('image')), 'image');
        assert.equal(api.mediaKey({mediaKey:'image'}), 'image');
    });

    test(`Umbraco ${major}: editing replaces only the selected media file`, async () => {
        const {api,media,calls} = fixture(major);
        const cropper = {src:'/media/test/plan.png', focalPoint:{left:0.2,top:0.8}, crops:[]};
        media.values = [{alias:'umbracoFile',editorAlias:'Umbraco.ImageCropper',value:JSON.stringify(cropper)},
            {alias:'altText',culture:'en-US',value:'A plan'}];
        media.variants = [{name:'Plan (1)',culture:'en-US',segment:null,updateDate:'2026-01-02'}];
        await api.updateImage('image','temporary-file');
        assert.deepEqual(calls,[['read',{path:{id:'image'}}],['update',{path:{id:'image'},body:{
            variants:[{name:'Plan (1)',culture:'en-US',segment:null}],
            values:[{...media.values[0],value:{...cropper,temporaryFileId:'temporary-file'}},media.values[1]]
        }}]]);
    });

    test(`Umbraco ${major}: media failures are reported to the editor`, async () => {
        const {api,services} = fixture(major);
        services.MediaService.putMediaById = async () => ({response:{ok:false}});
        await assert.rejects(api.updateImage('image','temp'), /could not update/);
        services.MediaService.getMediaById = async () => ({response:{ok:false},error:{status:403}});
        services.MediaService.postMedia = async () => ({response:{ok:false}});
        services.TemporaryFileService.postTemporaryFile = async () => ({error:{status:413}});
        await assert.rejects(api.readMedia('image'), /Cannot read/);
        await assert.rejects(api.upload(new Blob()), /could not upload/);
        await assert.rejects(api.createImage({id:'image',temporaryFileId:'temp'}), /could not create the diagram media/);
        services.MediaService.getTreeMediaRoot = async () => ({response:{ok:false},error:{status:403}});
        await assert.rejects(api.createImage({id:'image',temporaryFileId:'temp'}), /could not read the media library/);
    });
}

test('the default folder can already exist on a later page under its own key', async () => {
    const {api, root, calls} = fixture(18);
    const folder = {id:'existing',mediaType:{id:'f38bd2d7-65d0-48e6-95dc-87ce06ec2d3d'},variants:[{name:'DrawIO'}]};
    root.push(...Array(99).fill({...folder,parent:{id:'nested'}}), {...folder,isTrashed:true}, folder);
    assert.equal(await api.findDiagramFolder(), 'existing');
    assert.deepEqual(calls,[['root',0],['root',100]]);
});

test('without a drawio folder the editor creates only the image at the root', async () => {
    const {api, root, calls} = fixture(18);
    root.push({id:'file',mediaType:{id:'file-type'},variants:[{name:'drawio'}]});
    await api.createImage({id:'new-image',temporaryFileId:'file',name:'   '});
    assert.deepEqual(calls.map(([action]) => action),['root','create']);
    const image = calls[1][1].body;
    assert.equal(image.parent,null);
    assert.equal(image.mediaType.id,api.imageTypeIds[0]);
    assert.equal(image.variants[0].name,'diagram');
});

test('unsupported CMS versions never silently select another adapter', () => {
    for (const major of [16,19,undefined,'17']) assert.throws(() => createMediaApi(major,null,{}), /no tested media adapter/);
});
