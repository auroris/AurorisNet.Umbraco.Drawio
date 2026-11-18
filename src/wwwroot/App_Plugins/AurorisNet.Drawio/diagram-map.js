const svgNamespace = 'http://www.w3.org/2000/svg';
const xlink = 'http://www.w3.org/1999/xlink';

// draw.io has already applied page bounds, rotations and group positioning to the exported SVG.
// Keep just the linked outlines in those coordinates. Both image formats use the same viewBox.
function outline(element) {
    const n = name => Number(element.getAttribute(name));
    switch (element.localName) {
        case 'path': return element.getAttribute('d');
        case 'line': return `M ${n('x1')} ${n('y1')} L ${n('x2')} ${n('y2')}`;
        case 'polygon':
        case 'polyline': {
            const points = element.getAttribute('points');
            return points && 'M ' + points + (element.localName === 'polygon' ? ' Z' : '');
        }
        case 'circle':
        case 'ellipse': {
            const x = n('cx'), y = n('cy');
            const rx = n('r') || n('rx'), ry = n('r') || n('ry');
            return `M ${x-rx} ${y} a ${rx} ${ry} 0 1 0 ${2*rx} 0 a ${rx} ${ry} 0 1 0 ${-2*rx} 0 Z`;
        }
        case 'rect':
        case 'image': {
            const x = n('x'), y = n('y'), w = n('width'), h = n('height');
            const rx = Math.min(w / 2, n('rx') || n('ry')), ry = Math.min(h / 2, n('ry') || n('rx'));
            if (!rx || !ry) return `M ${x} ${y} h ${w} v ${h} h ${-w} Z`;
            return `M ${x+rx} ${y} h ${w-2*rx} a ${rx} ${ry} 0 0 1 ${rx} ${ry} v ${h-2*ry} a ${rx} ${ry} 0 0 1 ${-rx} ${ry} h ${2*rx-w} a ${rx} ${ry} 0 0 1 ${-rx} ${-ry} v ${2*ry-h} a ${rx} ${ry} 0 0 1 ${rx} ${-ry} Z`;
        }
    }
}

function region(element, root) {
    const d = outline(element);
    if (!d) return;
    const transforms = [];
    for (let node = element; node !== root; node = node.parentElement) {
        if (node.hasAttribute('transform')) transforms.unshift(node.getAttribute('transform'));
    }
    const closed = /[zZ]\s*$/.test(d);
    const line = element.localName === 'line' || element.localName === 'polyline'
        || (!closed && element.closest('[fill]')?.getAttribute('fill') === 'none');
    const width = Number(element.closest('[stroke-width]')?.getAttribute('stroke-width'));
    return {d, transform:transforms.join(' ') || undefined, strokeWidth:line ? Math.max(6, width || 1) : undefined};
}

export function extractDiagramMap(svg) {
    const xml = new DOMParser().parseFromString(svg, 'image/svg+xml');
    const root = xml.documentElement;
    if (root.localName !== 'svg' || root.namespaceURI !== svgNamespace || xml.querySelector('parsererror'))
        throw Error('draw.io returned an invalid SVG.');
    const bounds = root.viewBox.baseVal;
    if (!(bounds.width > 0 && bounds.height > 0)) throw Error('The diagram has no SVG viewBox.');
    const links = [];
    for (const anchor of root.getElementsByTagNameNS(svgNamespace, 'a')) {
        const href = anchor.getAttribute('href') || anchor.getAttributeNS(xlink, 'href');
        if (!href) continue;
        const shapes = [];
        for (const element of anchor.querySelectorAll('path,rect,circle,ellipse,line,polygon,polyline,image')) {
            if (element.namespaceURI !== svgNamespace || element.closest('a') !== anchor ||
                element.closest('defs,clipPath,mask,symbol,foreignObject,[display="none"],[visibility="hidden"]')) continue;
            const shape = region(element, root);
            if (shape) shapes.push(shape);
        }
        const label = anchor.querySelector('title,foreignObject,text')?.textContent.trim() || href;
        if (shapes.length) links.push({href, label, shapes, newTab:anchor.getAttribute('target') === '_blank' || undefined});
    }
    return links.length ? JSON.stringify({viewBox:[bounds.x, bounds.y, bounds.width, bounds.height].join(' '), links}) : null;
}
