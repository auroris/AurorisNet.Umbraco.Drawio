import {MediaService, TemporaryFileService} from '@umbraco-cms/backoffice/external/backend-api';
import {UmbMediaDetailRepository} from '@umbraco-cms/backoffice/media';
import {siteSettings} from '../bridge.js';
import {createMediaApi} from './umbraco-media-api.js';

let adapterSettings;
export async function mediaApi(host) {
    // Both values are fixed for this installation. The editor reads its mutable configuration separately.
    const {umbracoMajor, diagramFolderId} = await (adapterSettings ??= siteSettings()
        .then(({umbracoMajor, diagramFolderId}) => ({umbracoMajor, diagramFolderId}))
        .catch(error => { adapterSettings = undefined; throw error; }));
    return createMediaApi(umbracoMajor, host, {MediaService, TemporaryFileService, MediaDetailRepository:UmbMediaDetailRepository}, diagramFolderId);
}
