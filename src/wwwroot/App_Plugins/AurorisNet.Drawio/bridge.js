const prefix = 'data:image/png;base64,';
export const maxBytes = 40 * 1024 * 1024;
export function readMessage(event, source, origin) {
    if (!source || event.source !== source || event.origin !== origin || typeof event.data !== 'string') return;
    try { return JSON.parse(event.data); } catch { return; }
}
export function send(target, message, origin) { target.postMessage(JSON.stringify(message), origin); }
export async function siteSettings() {
    const response = await fetch(new URL('../../draw/settings', import.meta.url));
    if (!response.ok) throw Error('Cannot load the diagram editor settings.');
    return response.json();
}
export async function editorSettings(viewer = false) {
    const settings = await siteSettings();
    const url = new URL(settings.editorUrl, location.href);
    for (const [key, value] of Object.entries({embed:1, proto:'json', spin:1, configure:1, libraries:1})) url.searchParams.set(key, value);
    if (viewer) { url.searchParams.set('lightbox', '1'); url.searchParams.set('layers', '1'); url.searchParams.set('nav', '1'); }
    const configuration = settings.configuration || {};
    for (const group of configuration.libraries || []) for (const entry of group.entries || [])
        for (const library of entry.libs || []) library.url = new URL(library.url, location.origin).href;
    return { url, configuration };
}
export async function websiteDiagram(source) {
    const url = new URL(source, location.href);
    if (url.origin !== location.origin || !/\.(png|svg|drawio|xml|jpe?g|gif|webp)$/i.test(url.pathname))
        throw Error('Choose an image or .drawio file on this website.');
    // Read .drawio media through the media store to avoid its editor redirect, also with remote media storage.
    const site = new URL('../../', import.meta.url).pathname;
    let request = url;
    if (url.pathname.startsWith(site + 'media/') && /\.(drawio|xml|svg|png)$/i.test(url.pathname)) {
        request = new URL('../../draw/proxy', import.meta.url);
        request.searchParams.set('url', url.pathname.slice(site.length));
    }
    const response = await fetch(request, {redirect:'error'});
    if (!response.ok) throw Error(`Cannot load the diagram (${response.status}).`);
    const blob = await response.blob();
    if (blob.size > maxBytes) throw Error('The diagram exceeds 40 MiB.');
    const extension = url.pathname.split('.').pop().toLowerCase();
    if (extension === 'drawio' || extension === 'xml') {
        const xml = await blob.text();
        const document = new DOMParser().parseFromString(xml, 'application/xml');
        if (!['mxfile', 'mxGraphModel'].includes(document.documentElement.localName) || document.querySelector('parsererror'))
            throw Error('The file is not a draw.io diagram.');
        return {xml};
    }
    if (extension === 'svg') {
        const xml = await blob.text();
        const document = new DOMParser().parseFromString(xml, 'image/svg+xml');
        if (document.documentElement.localName !== 'svg' || document.querySelector('parsererror')) throw Error('Invalid SVG image.');
        const content = document.documentElement.getAttribute('content');
        if (content) return {xml:content};
    }
    const type = {png:'image/png', svg:'image/svg+xml', jpg:'image/jpeg', jpeg:'image/jpeg', gif:'image/gif', webp:'image/webp'}[extension];
    const data = await new Promise((resolve, reject) => {
        const reader = new FileReader(); reader.onload = () => resolve(reader.result); reader.onerror = reject;
        reader.readAsDataURL(new Blob([blob], {type}));
    });
    if (extension === 'png') { try { editableBytes(data); return {xmlpng:data}; } catch { /* A normal image is imported as one picture. */ } }
    const picture = new Image(); picture.src = data; await picture.decode();
    const xml = new DOMParser().parseFromString('<mxGraphModel><root><mxCell id="0"/><mxCell id="1" parent="0"/><mxCell id="2" vertex="1" parent="1"><mxGeometry as="geometry"/></mxCell></root></mxGraphModel>', 'application/xml');
    const cell = xml.querySelector('[id="2"]');
    cell.setAttribute('style', `shape=image;image=${data.replace(';base64,', ',')};`);
    const geometry = cell.firstElementChild;
    geometry.setAttribute('width', picture.naturalWidth); geometry.setAttribute('height', picture.naturalHeight);
    return {xml:new XMLSerializer().serializeToString(xml)};
}
export async function originalPng(url) {
    const response = await fetch(url);
    if (!response.ok) throw Error('The original diagram could not be loaded.');
    const blob = await response.blob();
    if (blob.size > maxBytes) throw Error('The diagram exceeds 40 MiB.');
    const data = await new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(reader.result);
        reader.onerror = () => reject(Error('Could not read the original diagram.'));
        reader.readAsDataURL(new Blob([blob], {type:'image/png'}));
    });
    editableBytes(data);
    return data;
}
export function editableBytes(data) {
    if (typeof data !== 'string' || !data.startsWith(prefix) || data.length - prefix.length > 4 * Math.ceil(maxBytes / 3))
        throw Error('Expected an editable PNG of at most 40 MiB.');
    const bytes = Uint8Array.from(atob(data.slice(prefix.length)), c => c.charCodeAt(0));
    const view = new DataView(bytes.buffer);
    if (bytes.length > maxBytes || bytes.length < 33 || view.getUint32(0) !== 0x89504e47 || view.getUint32(4) !== 0x0d0a1a0a)
        throw Error('Invalid diagram PNG.');
    let model = false;
    for (let i = 8; i + 12 <= bytes.length;) {
        const len = view.getUint32(i), end = i + 12 + len;
        if (end > bytes.length) break;
        const type = String.fromCharCode(...bytes.subarray(i+4,i+8));
        if (['tEXt','iTXt','zTXt'].includes(type)) {
            const key = String.fromCharCode(...bytes.subarray(i+8, i+8+Math.min(14,len))).split('\0')[0];
            if (key === 'mxfile' || key === 'mxGraphModel') model = true;
        }
        if (type === 'IEND' && model) return bytes;
        i = end;
    }
    throw Error('The PNG does not contain an editable diagram.');
}
