import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { html, css } from '@umbraco-cms/backoffice/external/lit';
import { UmbChangeEvent } from '@umbraco-cms/backoffice/event';
import { UMB_DOCUMENT_WORKSPACE_CONTEXT } from '@umbraco-cms/backoffice/document';
import { UMB_VARIANT_CONTEXT } from '@umbraco-cms/backoffice/variant';
import { UMB_ACTION_EVENT_CONTEXT } from '@umbraco-cms/backoffice/action';
import { UmbEntityUpdatedEvent } from '@umbraco-cms/backoffice/entity-action';
import '@umbraco-cms/backoffice/media';
import { editableBytes, originalPng, readMessage, send, editorSettings } from './bridge.js';
import { mediaApi } from './compatibility/media-api.js';

/** The property editor UI for the diagram data type: a single-image media picker with a button that opens draw.io. */
export default class DiagramEditor extends UmbLitElement {
    static properties = { value:{attribute:false}, readonly:{type:Boolean}, open:{state:true}, error:{state:true}, settings:{state:true}, api:{state:true} };
    static styles = css`
        :host { display: block; }
        button { font: inherit; padding: .45rem .8rem; cursor: pointer; }
        dialog {
            position: fixed;
            inset: 0;
            width: 100%;
            height: 100%;
            max-width: none;
            max-height: none;
            margin: 0;
            border: 0;
            padding: 0;
            overflow: hidden;
        }
        dialog[open] { display: flex; flex-direction: column; }
        iframe { flex: 1; min-height: 0; width: 100%; border: 0; }
        [role=alert] { color: #9d1721; }
        dialog [role=alert]:not(:empty) { padding: .4rem .8rem; }
    `;
    constructor() {
        super();
        // A Diagram block provides its own workspace between this editor and the page.
        this.consumeContext(UMB_DOCUMENT_WORKSPACE_CONTEXT, context => { this.documentWorkspace = context; }).passContextAliasMatches();
        this.consumeContext(UMB_VARIANT_CONTEXT, context => { this.variantContext = context; });
        this.consumeContext(UMB_ACTION_EVENT_CONTEXT, context => { this.actionEvents = context; });
    }
    async pageName() {
        const variant = await this.variantContext?.getVariantId();
        const workspace = this.documentWorkspace;
        return workspace?.getName(variant)?.trim() || workspace?.getName()?.trim()
            || workspace?.getVariants().find(item => item.name?.trim())?.name.trim();
    }
    connectedCallback() {
        super.connectedCallback(); window.addEventListener('message', this.onMessage);
        mediaApi(this).then(api => { this.api = api; }).catch(error => { this.error = error.message; });
    }
    disconnectedCallback() { this.close(); window.removeEventListener('message',this.onMessage); super.disconnectedCallback(); }
    close() {
        this.shadowRoot?.querySelector('dialog')?.close();
        this.session = null; this.open = false; this.error = '';
    }
    onCancel(event) {
        event.preventDefault();
        // Escape can dismiss a frame that failed to load. Once editing, draw.io handles cancellation and unsaved work.
        if (!this.session?.loaded || this.readonly) this.close();
    }
    async launch() {
        if (this.readonly || !this.api) return;
        const selected = this.api.mediaKey(this.value);
        const session = this.session = {loaded:false, id:selected || crypto.randomUUID(), isNew:!selected}; this.error = '';
        try {
            const settings = await editorSettings();
            if (this.session !== session) return;
            this.settings = settings;
            this.open = true;
            await this.updateComplete;
            if (this.session === session) this.shadowRoot.querySelector('dialog').showModal();
        } catch (error) {
            if (this.session !== session) return;
            this.close();
            this.error = error.message;
        }
    }
    post(frame, message) { send(frame, message, this.settings.url.origin); }
    changeValue(value) { this.value = value; this.dispatchEvent(new UmbChangeEvent()); }
    async load(session, frame) {
        let png;
        if (!session.isNew) {
            const media = await this.api.readMedia(session.id);
            if (!media.url) throw Error('The selected media has no original file.');
            session.fileName = decodeURIComponent(new URL(media.url, document.baseURI).pathname.split('/').pop());
            png = await originalPng(media.url);
        }
        if (this.session !== session) return;
        session.sent = true;
        this.post(frame, png ? {action:'load',xmlpng:png} : {action:'load'});
    }
    async store(png, session) {
        const {id} = session;
        // Only newly created files get the package's retention marker. Keep the filename of a picked diagram.
        const fileName = session.fileName ?? `diagram-${id.replaceAll('-', '')}.png`;
        const file = new File([editableBytes(png)], fileName, {type:'image/png'});
        const temporaryFileId = await this.api.upload(file);
        if (session !== this.session || this.readonly) return;
        if (session.isNew) {
            await this.api.createImage({id, temporaryFileId, name:await this.pageName()});
            if (session !== this.session || this.readonly) return;
            this.changeValue(this.api.pickMedia(id, this.value));
        } else {
            await this.api.updateImage(id, temporaryFileId);
            this.actionEvents?.dispatchEvent(new UmbEntityUpdatedEvent({entityType:'media', unique:id}));
        }
        if (session === this.session) this.close();
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
            session.uploading = session.saving = false;
            this.error = err?.message || err?.detail || err?.title || 'The diagram editor could not complete the request.';
            this.post(frame, {action:'spinner',show:false}); this.post(frame, {action:'status',modified:true});
        }
    };
    render() {
        return html`<umb-input-rich-media .value=${this.value || []} .allowedContentTypeIds=${this.api?.imageTypeIds || []} .max=${1} .readonly=${this.readonly || !this.api}
            @change=${event => { event.stopPropagation(); if (!this.readonly) this.changeValue(event.target.value); }}></umb-input-rich-media>
            <button type="button" ?disabled=${this.readonly || !this.api} @click=${this.launch}>${this.value?.length ? "Edit diagram" : "Create diagram"}</button>
            <span role="alert">${this.open ? '' : this.error}</span>
            ${this.open ? html`<dialog aria-label="Diagram editor" @cancel=${this.onCancel}>
                <span role="alert">${this.error}</span>
                <iframe title="Diagram editor" referrerpolicy="no-referrer" src=${this.settings.url.href}></iframe>
            </dialog>` : ""}`;
    }
}
customElements.define('aurorisnet-drawio-editor', DiagramEditor);
