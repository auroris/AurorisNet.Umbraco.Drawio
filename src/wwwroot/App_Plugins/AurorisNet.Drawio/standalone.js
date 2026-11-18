import {editorSettings, websiteDiagram, readMessage, send} from './bridge.js';

const viewer = document.body.dataset.viewer === 'true';
const frame = document.querySelector('iframe');
const status = document.querySelector('#status');
const parameters = new URL(location.href).searchParams;
const source = parameters.get('url');
function sourceName() {
    return source ? decodeURIComponent(new URL(source, location.href).pathname.split('/').pop()) : '';
}
let settings, loaded = false, started = false, modified = false;
function report(error) { status.textContent = error.message || 'The diagram could not be loaded.'; }
window.addEventListener('beforeunload', event => { if (modified) { event.preventDefault(); event.returnValue = ''; } });
window.addEventListener('message', async event => {
    if (!settings) return;
    const message = readMessage(event, frame.contentWindow, settings.url.origin);
    if (!message) return;
    const post = data => send(frame.contentWindow, data, settings.url.origin);
    try {
        if (message.event === 'configure') post({action:'configure', config:settings.configuration});
        if (message.event === 'init' && !started) {
            started = true;
            if (viewer && !source) throw Error('Supply the diagram file in the url parameter.');
            const diagram = source ? await websiteDiagram(source) : {};
            post({action:'load', ...diagram, title:sourceName() || 'Diagram', autosave:!viewer, noExitBtn:1});
        }
        if (message.event === 'load') { loaded = true; status.textContent = ''; }
        if (message.event === 'autosave' && !viewer) modified = true;
        if (message.event === 'save' && loaded && !viewer) {
            if (typeof message.xml !== 'string') throw Error('The editor returned no diagram.');
            const url = URL.createObjectURL(new Blob([message.xml], {type:'application/xml'}));
            const link = document.createElement('a'); link.href = url;
            link.download = (sourceName().replace(/\.[^.]+$/, '') || 'diagram') + '.drawio';
            link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
            modified = false; post({action:'status', modified:false});
            status.textContent = 'Downloaded the diagram. The website’s source file is unchanged.';
        }
        if (message.event === 'error') throw Error(message.message || message.error || 'The editor could not open the diagram.');
    } catch (error) { report(error); post({action:'spinner', show:false}); }
});
try {
    settings = await editorSettings(viewer);
    settings.url.searchParams.set('noExitBtn', '1');
    if (parameters.get('lang')) settings.url.searchParams.set('lang', parameters.get('lang'));
    frame.src = settings.url.href;
    setTimeout(() => { if (!started) report(Error('The editor has not responded. Check the connection and the site’s frame policy.')); }, 30000);
} catch (error) { report(error); }
