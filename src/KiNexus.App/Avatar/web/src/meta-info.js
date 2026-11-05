// Übersetzt die Nutzungsregeln aus der VRM-Datei in lesbare deutsche Zeilen.
// VRM 0.x und VRM 1.0 speichern diese Infos verschieden.

const NOT_SET = 'nicht angegeben';

const PERMISSION_V1 = {
  onlyAuthor: 'nur der Autor',
  onlySeparatelyLicensedPerson: 'nur Personen mit eigener Erlaubnis',
  everyone: 'alle',
};
const COMMERCIAL_V1 = {
  personalNonProfit: 'nur privat, ohne Gewinn',
  personalProfit: 'privat, auch mit Gewinn',
  corporation: 'auch Firmen',
};
const MODIFICATION_V1 = {
  prohibited: 'verboten',
  allowModification: 'erlaubt',
  allowModificationRedistribution: 'erlaubt, auch weitergeben',
};
const CREDIT_V1 = {
  required: 'nötig',
  unnecessary: 'nicht nötig',
};
const PERMISSION_V0 = {
  OnlyAuthor: 'nur der Autor',
  ExplicitlyLicensedPerson: 'nur Personen mit eigener Erlaubnis',
  Everyone: 'alle',
};
const ALLOW_V0 = {
  Allow: 'erlaubt',
  Disallow: 'nicht erlaubt',
};

function yesNo(value) {
  if (value === true) return 'erlaubt';
  if (value === false) return 'nicht erlaubt';
  return NOT_SET;
}

function pick(map, value) {
  if (value == null || value === '') return NOT_SET;
  return map[value] || String(value);
}

function text(value) {
  if (value == null || value === '') return NOT_SET;
  return String(value);
}

export function describeMeta(meta) {
  if (!meta) {
    return { version: 'unbekannt', title: '', rows: [] };
  }

  if (meta.metaVersion === '1') {
    return {
      version: 'VRM 1.0',
      title: text(meta.name),
      rows: [
        ['Name', text(meta.name)],
        ['Version', text(meta.version)],
        ['Autor', meta.authors && meta.authors.length ? meta.authors.join(', ') : NOT_SET],
        ['Wer darf ihn nutzen?', pick(PERMISSION_V1, meta.avatarPermission)],
        ['Kommerzielle Nutzung', pick(COMMERCIAL_V1, meta.commercialUsage)],
        ['Gewalt-Darstellung', yesNo(meta.allowExcessivelyViolentUsage)],
        ['Sexuelle Darstellung', yesNo(meta.allowExcessivelySexualUsage)],
        ['Politik / Religion', yesNo(meta.allowPoliticalOrReligiousUsage)],
        ['Verändern', pick(MODIFICATION_V1, meta.modification)],
        ['Weitergeben', yesNo(meta.allowRedistribution)],
        ['Namensnennung', pick(CREDIT_V1, meta.creditNotation)],
        ['Lizenz', text(meta.licenseUrl)],
        ['Weitere Lizenz', text(meta.otherLicenseUrl)],
      ],
    };
  }

  return {
    version: 'VRM 0.x',
    title: text(meta.title),
    rows: [
      ['Titel', text(meta.title)],
      ['Version', text(meta.version)],
      ['Autor', text(meta.author)],
      ['Wer darf ihn nutzen?', pick(PERMISSION_V0, meta.allowedUserName)],
      ['Kommerzielle Nutzung', pick(ALLOW_V0, meta.commercialUssageName)],
      ['Gewalt-Darstellung', pick(ALLOW_V0, meta.violentUssageName)],
      ['Sexuelle Darstellung', pick(ALLOW_V0, meta.sexualUssageName)],
      ['Lizenz', text(meta.licenseName)],
      ['Weitere Lizenz', text(meta.otherLicenseUrl)],
    ],
  };
}
