import {UmbLitElement} from '@umbraco-cms/backoffice/lit-element';
import {html} from '@umbraco-cms/backoffice/external/lit';
import {UmbChangeEvent} from '@umbraco-cms/backoffice/event';

// Keep the existing storage format: only an explicit 0 enables the viewer. An unset value is off.
export default class DiagramClickAction extends UmbLitElement {
    static properties = {value:{attribute:false}, readonly:{type:Boolean}};
    render() {
        return html`<uui-toggle label="Open in Viewer" .checked=${this.value === 0 || this.value === '0'}
            ?disabled=${this.readonly} @change=${event => {
                this.value = event.target.checked ? 0 : 1;
                this.dispatchEvent(new UmbChangeEvent());
            }}></uui-toggle>`;
    }
}
customElements.define('aurorisnet-drawio-click-action', DiagramClickAction);
