import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { html, css } from '@umbraco-cms/backoffice/external/lit';
import { UmbChangeEvent } from '@umbraco-cms/backoffice/event';
import '@umbraco-cms/backoffice/media';
import { editableBytes, originalPng, readMessage, send, editorSettings } from './bridge.js';
import { mediaApi } from './compatibility/media-api.js';

/** The property editor UI for the diagram data type: a single-image media picker with a button that opens draw.io. */
export default class DiagramEditor extends UmbLitElement {
    static properties = { value:{attribute:false}, readonly:{type:Boolean}, open:{state:true}, error:{state:true}, settings:{state:true}, api:{state:true} };
    static styles = css`:host{display:block}button{font:inherit;padding:.45rem .8rem;cursor:pointer}.panel{position:fixed;inset:1rem;z-index:10000;background:white;display:flex;flex-direction:column;border:1px solid #aaa;box-shadow:0 0 0 100vmax #0006}.panel header{display:flex;align-items:center;justify-content:space-between;padding:.4rem .8rem}.panel iframe{flex:1;border:0;width:100%}[role=alert]{color:#9d1721}`;
    connectedCallback() {
        super.connectedCallback(); window.addEventListener('message', this.onMessage);
        mediaApi(this).then(api => { this.api = api; }).catch(error => { this.error = error.message; });
    }
    disconnectedCallback() { this.close(); window.removeEventListener('message',this.onMessage); super.disconnectedCallback(); }
    close() { this.session = null; this.open = false; this.error = ''; }
    async launch() {
        if (this.readonly || !this.api) return;
        const session = this.session = {loaded:false}; this.error = '';
        try { const settings = await editorSettings(); if (this.session !== session) return; this.settings = settings; this.open = true; }
        catch (error) { this.error = error.message; }
    }
    post(frame, message) { send(frame, message, this.settings.url.origin); }
    changeValue(value) { this.value = value; this.dispatchEvent(new UmbChangeEvent()); }
    async load(session, frame) {
        const id = this.api.mediaKey(this.value);
        let png;
        if (id) {
            const media = await this.api.readMedia(id);
            if (!media.url) throw Error('The selected media has no original file.');
            session.parent = media.parent;
            session.name = media.name;
            png = await originalPng(media.url);
        }
        if (this.session !== session) return;
        session.sent = true;
        this.post(frame, png ? {action:'load',xmlpng:png} : {action:'load'});
    }
    async store(png, session) {
        // Each save creates a media item. Its file name carries its key, which marks it as the editor's own, so the server
        // can remove it after the final content version referencing it is deleted.
        const id = crypto.randomUUID();
        const file = new File([editableBytes(png)], `diagram-${id.replaceAll('-', '')}.png`, {type:'image/png'});
        const temporaryFileId = await this.api.upload(file);
        if (session !== this.session || this.readonly) return;
        await this.api.createImage({id, temporaryFileId, parent:session.parent, name:session.name});
        if (session !== this.session || this.readonly) return;
        this.changeValue(this.api.pickMedia(id, this.value));
        this.close();
    }
    onMessage = async event => {
        const frame = this.shadowRoot?.querySelector('iframe')?.contentWindow;
        const msg = readMessage(event, frame, this.settings?.url.origin), session = this.session;
        if (!msg || !session || this.readonly) return;
        try {
            if (msg.event === 'configure') this.post(frame, {action:'configure',config:this.settings.configuration});
            else if (msg.event === 'init' && !session.init) { session.init = true; await this.load(session, frame); }
            else if (msg.event === 'load' && session.sent) session.loaded = true;
            else if (msg.event === 'save' && session.loaded && !session.saving) {
                session.saving = true; this.error = ''; this.post(frame, {action:'export',format:'xmlpng',spinKey:'saving'});
            } else if (msg.event === 'export' && session.saving && !session.uploading) {
                session.uploading = true; await this.store(msg.data, session);
            } else if (msg.event === 'exit' && !session.uploading) this.close();
        } catch (err) {
            if (session !== this.session) return;
            session.uploading = session.saving = false; this.error = err.message;
            this.post(frame, {action:'spinner',show:false}); this.post(frame, {action:'status',modified:true});
        }
    };
    render() {
        return html`<umb-input-rich-media .value=${this.value || []} .allowedContentTypeIds=${this.api?.imageTypeIds || []} .max=${1} .readonly=${this.readonly || !this.api}
            @change=${event => { event.stopPropagation(); if (!this.readonly) this.changeValue(event.target.value); }}></umb-input-rich-media>
            <button type="button" ?disabled=${this.readonly || !this.api} @click=${this.launch}>${this.value?.length ? "Edit diagram" : "Create diagram"}</button>
            <span role="alert">${this.open ? '' : this.error}</span>
            ${this.open ? html`<div class="panel" role="dialog" aria-label="Diagram editor"><header><strong>Diagram</strong><span role="alert">${this.error}</span>
            <button type="button" @click=${this.close}>Close</button></header><iframe title="Diagram editor" referrerpolicy="no-referrer" src=${this.settings.url.href}></iframe></div>` : ""}`;
    }
}
customElements.define('aurorisnet-drawio-editor', DiagramEditor);
