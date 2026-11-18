import {UmbLitElement} from '@umbraco-cms/backoffice/lit-element';
import {html} from '@umbraco-cms/backoffice/external/lit';
import {UmbChangeEvent} from '@umbraco-cms/backoffice/event';

// Sites installed with the original integer property retain all three click actions.
export default class DiagramClickAction extends UmbLitElement {
    static properties = {value:{attribute:false}, readonly:{type:Boolean}};
    render() {
        const selected = this.value == null ? 2 : Number(this.value);
        return html`<select aria-label="Click action"
            ?disabled=${this.readonly} @change=${event => {
                this.value = Number(event.target.value);
                this.dispatchEvent(new UmbChangeEvent());
            }}>
            <option value="0" ?selected=${selected === 0}>Open in viewer</option>
            <option value="1" ?selected=${selected === 1}>Follow link below</option>
            <option value="2" ?selected=${selected === 2}>No link</option>
        </select>`;
    }
}
customElements.define('aurorisnet-drawio-click-action', DiagramClickAction);
