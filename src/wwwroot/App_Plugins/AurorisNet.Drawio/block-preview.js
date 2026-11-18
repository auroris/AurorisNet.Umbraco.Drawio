import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { html, css, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UMB_ACTION_EVENT_CONTEXT } from '@umbraco-cms/backoffice/action';
import { UmbEntityUpdatedEvent } from '@umbraco-cms/backoffice/entity-action';
import { mediaApi } from './compatibility/media-api.js';

/**
 * The drawioDiagram block's preview in a Block Grid or Block List: the selected image format and caption, so editors see the
 * drawing instead of the block's name. Selecting the preview opens the block, as the default view does.
 */
export default class DiagramPreview extends UmbLitElement {
    static properties = Object.fromEntries(['content', 'config', 'label', 'icon', 'unpublished'].map(name => [name, { attribute: false }]));
    #api;
    #apiError = false;
    #events;
    // Editing a diagram replaces the file of the same media item, so the picture is read again when that item changes.
    #onUpdated = event => { if (this.#key && event.getUnique() === this.#key) this.#load(this.#key); };
    constructor() {
        super();
        this.consumeContext(UMB_ACTION_EVENT_CONTEXT, events => {
            this.#events?.removeEventListener(UmbEntityUpdatedEvent.TYPE, this.#onUpdated);
            this.#events = events;
            if (this.isConnected) events?.addEventListener(UmbEntityUpdatedEvent.TYPE, this.#onUpdated);
        });
    }
    connectedCallback() {
        super.connectedCallback();
        this.#events?.addEventListener(UmbEntityUpdatedEvent.TYPE, this.#onUpdated);
        mediaApi(this).then(api => { this.#api = api; this.requestUpdate(); }).catch(() => { this.#apiError = true; this.requestUpdate(); });
    }
    disconnectedCallback() {
        this.#events?.removeEventListener(UmbEntityUpdatedEvent.TYPE, this.#onUpdated);
        super.disconnectedCallback();
    }
    #key;
    #media;
    async #load(key) {
        this.#key = key;
        this.#media = undefined;
        if (!key) return;
        try {
            const media = await this.#api.readPreview(key);
            if (this.#key !== key) return;
            this.#media = media;
        } catch { if (this.#key === key) this.#media = null; }
        this.requestUpdate();
    }
    render() {
        const image = this.content?.image;
        const key = this.#api?.mediaKey(image);
        if (key !== this.#key) this.#load(key);
        const url = this.content?.format === true ? this.#media?.svgUrl : this.#media?.url;
        const caption = this.content?.caption;
        const body = !this.#api ? html`<p class="note">${this.#apiError ? 'The diagram is unavailable.' : 'Loading the diagram…'}</p>`
            : !key ? html`<p class="note">Choose or create a diagram.</p>`
            : url ? html`<img src=${url} alt=${this.content?.altText || caption || ''}>`
            : html`<p class="note">${this.#media !== undefined ? 'The diagram is unavailable.' : 'Loading the diagram…'}</p>`;
        const edit = this.config?.showContentEdit ? this.config.editContentPath : undefined;
        return html`<a class="block" href=${edit ?? nothing} aria-label=${this.label || 'Diagram'}>
            <span class="name"><umb-icon .name=${this.icon || 'icon-diagram'}></umb-icon> ${this.label || 'Diagram'}
            ${this.unpublished ? html`<uui-tag look="secondary">Not published</uui-tag>` : nothing}</span>
            <figure>${body}${caption ? html`<figcaption>${caption}</figcaption>` : nothing}</figure>
        </a>`;
    }
    static styles = css`
        :host{display:block;min-width:0}
        .block{display:block;color:inherit;text-decoration:none;background:var(--uui-color-surface);border:1px solid var(--uui-color-border);border-radius:4px;padding:8px 12px}
        .block[href]:hover{border-color:var(--uui-color-interactive)}
        .name{display:flex;align-items:center;gap:6px;font-weight:600;margin-bottom:6px}
        figure{margin:0}img{display:block;max-width:100%;max-height:320px;height:auto;object-fit:contain}
        figcaption,.note{color:var(--uui-color-text-alt);font-size:12px;margin:6px 0 0}
    `;
}
customElements.define('aurorisnet-drawio-preview', DiagramPreview);
