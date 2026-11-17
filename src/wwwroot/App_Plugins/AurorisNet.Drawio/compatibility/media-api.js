import {MediaService, TemporaryFileService} from '@umbraco-cms/backoffice/external/backend-api';
import {UmbMediaDetailRepository} from '@umbraco-cms/backoffice/media';
import {siteSettings} from '../bridge.js';
import {createMediaApi} from './umbraco-media-api.js';

let version;
export async function mediaApi(host) {
    // The CMS version is fixed for this backoffice session. Do not cache mutable editor configuration.
    const major = await (version ??= siteSettings().then(settings => settings.umbracoMajor).catch(error => { version = undefined; throw error; }));
    return createMediaApi(major, host, {MediaService, TemporaryFileService, MediaDetailRepository:UmbMediaDetailRepository});
}
