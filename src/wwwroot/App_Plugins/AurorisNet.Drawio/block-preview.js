import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { html, css, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbMediaDetailRepository } from '@umbraco-cms/backoffice/media';

/**
 * The drawioDiagram block's preview in a Block Grid or Block List: the diagram's PNG and caption, so editors see the
 * drawing instead of the block's name. Selecting the preview opens the block, as the default view does.
 */
export default class DiagramPreview extends UmbLitElement {
    static properties = Object.fromEntries(['content', 'config', 'label', 'icon', 'unpublished'].map(name => [name, { attribute: false }]));
    #repository = new UmbMediaDetailRepository(this);
    #key;
    #url;
    async #load(key) {
        this.#key = key;
        this.#url = undefined;
        if (!key) return;
        try {
            const { data } = await this.#repository.requestByUnique(key);
            if (this.#key !== key) return;
            let file = data?.values?.find(value => value.alias === 'umbracoFile')?.value;
            if (typeof file === 'string' && file.trim().startsWith('{')) file = JSON.parse(file);
            this.#url = typeof file === 'string' ? file : file?.src;
        } catch { this.#url = null; }
        this.requestUpdate();
    }
    render() {
        const image = this.content?.image;
        const key = Array.isArray(image) ? image[0]?.mediaKey : image?.mediaKey;
        if (key !== this.#key) this.#load(key);
        const caption = this.content?.caption;
        const body = !key ? html`<p class="note">Choose or create a diagram.</p>`
            : this.#url ? html`<img src=${this.#url} alt=${this.content?.altText || caption || ''}>`
            : html`<p class="note">${this.#url === null ? 'The diagram is unavailable.' : 'Loading the diagram…'}</p>`;
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
