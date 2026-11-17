import {UmbLitElement} from '@umbraco-cms/backoffice/lit-element';
import {html} from '@umbraco-cms/backoffice/external/lit';
import {UmbChangeEvent} from '@umbraco-cms/backoffice/event';

// Numeric values match DiagramClickAction. Creating a block writes the viewer default explicitly.
export default class DiagramClickAction extends UmbLitElement {
    static properties = {value:{attribute:false}, readonly:{type:Boolean}};
    firstUpdated() {
        if (this.value == null && !this.readonly) { this.value = 0; this.dispatchEvent(new UmbChangeEvent()); }
    }
    render() {
        return html`<select aria-label="Click action" ?disabled=${this.readonly} @change=${event => {
            this.value = Number(event.target.value); this.dispatchEvent(new UmbChangeEvent());
        }}>${[[0,'Open diagram viewer'],[1,'Follow link below'],[2,'No link']].map(([value,label]) =>
            html`<option value=${value} ?selected=${value === (this.value ?? 0)}>${label}</option>`)}</select>`;
    }
}
customElements.define('aurorisnet-drawio-click-action', DiagramClickAction);
