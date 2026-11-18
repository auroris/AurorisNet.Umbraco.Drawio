import DOMPurify from './vendor/dompurify/purify.es.mjs';
import {diagramBlob} from './bridge.js';

const svgNamespace = 'http://www.w3.org/2000/svg';
const xlink = 'http://www.w3.org/1999/xlink';
const labelTags = 'a b blockquote br code div em font h1 h2 h3 h4 h5 h6 hr i img li ol p pre s small span strong sub sup table tbody td th thead tr u ul'.split(' ');
const config = {
    IN_PLACE:true,
    USE_PROFILES:{svg:true, svgFilters:true},
    ADD_TAGS:['foreignObject', 'use', ...labelTags],
    HTML_INTEGRATION_POINTS:{foreignobject:true},
    ADD_ATTR:['target', 'src', 'colspan', 'rowspan', 'dir', 'face', 'size', 'data-cell-id', 'requiredFeatures'],
    FORBID_TAGS:['style', 'animateColor', 'animateMotion', 'animateTransform', 'feImage', 'metadata'],
    FORBID_ATTR:['name', 'content', 'xml:base'],
    ALLOW_DATA_ATTR:false
};

// Keep draw.io's typography and label layout without letting a media file position elements across the host page.
const labelStyles = new Set(('align-items align-self background-color border border-color border-style border-width box-sizing ' +
    'color color-scheme display flex-direction flex-wrap font-family font-size font-style font-weight height justify-content ' +
    'line-height margin margin-bottom margin-left margin-right margin-top max-height max-width min-height min-width ' +
    'opacity overflow overflow-wrap padding padding-bottom padding-left padding-right padding-top pointer-events text-align ' +
    'text-decoration text-overflow transform transform-origin vertical-align white-space width word-break word-wrap').split(' '));
const presentation = new Set(('fill fill-opacity fill-rule stroke stroke-width stroke-opacity stroke-linecap stroke-linejoin ' +
    'stroke-dasharray stroke-dashoffset stop-color stop-opacity flood-color flood-opacity color font-size font-family ' +
    'font-weight font-style text-anchor shape-rendering clip-path filter mask').split(' '));
const refAttributes = ['fill', 'stroke', 'filter', 'mask', 'clip-path', 'marker-start', 'marker-mid', 'marker-end'];

function safeLink(href) {
    if (!href || href !== href.trim() || /[\\\u0000-\u001f\u007f]/.test(href) || href.startsWith('//')) return false;
    try { return ['http:', 'https:', 'mailto:', 'tel:'].includes(new URL(href, document.baseURI).protocol); }
    catch { return false; }
}

function prepareSvg(source, image, hasBackgroundLink) {
    const doc = new DOMParser().parseFromString(source, 'image/svg+xml');
    const svg = doc.documentElement;
    if (svg.namespaceURI !== svgNamespace || svg.localName !== 'svg' || doc.querySelector('parsererror') || !DOMPurify.isSupported)
        throw Error('Invalid diagram SVG.');
    // draw.io places its adaptive canvas color in a root-scoped stylesheet. Keep the color without importing page-wide CSS.
    const background = Array.from(svg.querySelectorAll('style'), sheet =>
        /--ge-adaptive-bg:\s*([^;{}]+);/.exec(sheet.textContent)?.[1]).find(value => value && CSS.supports('color', value));
    DOMPurify.sanitize(svg, config);

    // Gradients, clipping paths and arrowheads have document-wide IDs once the SVG is inline.
    const ids = new Map();
    const prefix = 'drawio-' + crypto.getRandomValues(new Uint32Array(2)).join('-') + '-';
    for (const [index, node] of svg.querySelectorAll('[id]').entries()) {
        const id = prefix + index;
        if (!ids.has(node.id)) ids.set(node.id, id);
        node.id = id;
    }
    svg.removeAttribute('id');
    for (const node of [svg, ...svg.querySelectorAll('*')]) {
        const style = node.style;
        if (style) {
            for (const property of Array.from(style)) {
                // Presentation attributes preserve exported colors while allowing normal page CSS to override them.
                if (node.namespaceURI === svgNamespace && presentation.has(property)) {
                    node.setAttribute(property, style.getPropertyValue(property));
                    style.removeProperty(property);
                } else if (!labelStyles.has(property)) style.removeProperty(property);
            }
        }
        for (const attr of refAttributes) {
            const value = node.getAttribute(attr);
            if (!value || !value.includes('url(')) continue;
            const local = /^url\(\s*['"]?#([^)'"]+)['"]?\s*\)$/.exec(value);
            if (local && ids.has(local[1])) node.setAttribute(attr, 'url(#' + ids.get(local[1]) + ')');
            else node.removeAttribute(attr);
        }
        for (const attr of ['aria-labelledby', 'aria-describedby']) {
            if (!node.hasAttribute(attr)) continue;
            const refs = node.getAttribute(attr).split(/\s+/).map(id => ids.get(id)).filter(Boolean);
            if (refs.length) node.setAttribute(attr, refs.join(' '));
            else node.removeAttribute(attr);
        }
        const href = node.getAttribute('href') || node.getAttributeNS(xlink, 'href');
        if (href) {
            node.removeAttributeNS(xlink, 'href');
            if (href.startsWith('#') && ids.has(href.slice(1))) node.setAttribute('href', '#' + ids.get(href.slice(1)));
            else if ((node.localName === 'a' && safeLink(href)) || node.localName === 'image') node.setAttribute('href', href);
            else node.removeAttribute('href');
        }
        if (node.localName === 'a') {
            if (node.getAttribute('target') === '_blank') node.setAttribute('rel', 'noopener');
            else node.removeAttribute('target');
        }
        // A native link underneath the SVG handles the rest of the image, including middle-click and keyboard navigation.
        if (hasBackgroundLink && style) {
            if (node.matches('a[href]')) style.pointerEvents = 'auto';
            else if (!node.closest('a[href]')) style.pointerEvents = 'none';
        }
    }
    svg.setAttribute('role', 'group');
    svg.setAttribute('aria-label', image.alt);
    if (background) svg.style.setProperty('--ge-adaptive-bg', background);
    svg.style.display = 'block';
    svg.style.maxWidth = '100%';
    svg.style.height = 'auto';
    svg.style.position = 'relative';
    svg.style.overflow = 'hidden';
    return svg;
}

class DiagramImage extends HTMLElement {
    async connectedCallback() {
        if (this.dataset.inlineSvg !== 'true' || this.loading) return;
        const image = this.querySelector('img');
        if (!image) return;
        this.loading = true;
        try {
            // Use the media URL so that the site's normal access checks, storage provider and cache still apply.
            const response = await fetch(image.src);
            if (!response.ok) return;
            const link = this.querySelector(':scope > a');
            const svg = prepareSvg(await (await diagramBlob(response)).text(), image, Boolean(link));
            if (link) {
                link.setAttribute('aria-label', link.getAttribute('aria-label') || image.alt);
                link.replaceChildren();
                link.style.position = 'absolute';
                link.style.inset = '0';
                this.replaceChildren(link, svg);
            } else this.replaceChildren(svg);
        } catch {
            // The server-rendered image and its shape links remain usable if inline rendering is unavailable.
        } finally { this.loading = false; }
    }
}

customElements.define('drawio-diagram-image', DiagramImage);
