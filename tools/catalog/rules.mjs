// Правила формата каталога твиков v1. Описание формата: docs/catalog-format-v1.md.

export const CATEGORIES = ['power', 'cpu', 'timer', 'scheduler', 'input', 'network', 'gaming', 'graphics',
  'nvidia', 'amd', 'services', 'tasks', 'privacy', 'cleanup'];

const ENUMS = {
  evidence: ['proven', 'situational', 'experimental', 'avoid'],
  risk: ['low', 'medium', 'high'],
  reboot: ['none', 'explorer', 'logoff', 'reboot'],
  vendor: ['any', 'nvidia', 'amd', 'intel'],
  kind: ['toggle', 'value', 'select', 'info'],
  category: CATEGORIES,
};
const OS = ['win10', 'win11'];
const REQUIRED = ['id', 'category', 'title', 'description', 'evidence', 'risk', 'reboot', 'os', 'vendor', 'kind',
  'apply', 'revert', 'sources'];
const OPTIONAL = ['recommendation', 'evidenceNote', 'riskNote', 'osNote', 'options', 'detect'];
const NULLABLE_STRINGS = ['recommendation', 'evidenceNote', 'riskNote', 'osNote'];

// Копия списка из docs/catalog-format-v1.md §7; C#-класс SafetyGuard держит такую же.
export const FORBIDDEN_SERVICES = [
  'vgc', 'vgk', 'FACEIT', 'FACEITService', 'EasyAntiCheat', 'EasyAntiCheat_EOS', 'BEService', 'BEDaisy',
  'TabletInputService', 'TextInputManagementService',
  'BFE', 'mpssvc', 'Dhcp', 'Dnscache', 'nsi', 'NlaSvc', 'netprofm', 'Wcmsvc', 'iphlpsvc', 'WinHttpAutoProxySvc',
  'CryptSvc', 'KeyIso',
  'RpcSs', 'RpcEptMapper', 'DcomLaunch', 'Winmgmt', 'EventLog', 'Schedule', 'ProfSvc', 'Power', 'PlugPlay',
  'BrokerInfrastructure', 'SystemEventsBroker', 'TimeBrokerSvc', 'CoreMessagingRegistrar', 'StateRepository',
  'LSM', 'SamSs', 'gpsvc', 'UserManager', 'Themes', 'FontCache', 'DispBrokerDesktopSvc',
  'Audiosrv', 'AudioEndpointBuilder', 'hidserv', 'MMCSS', 'GameInputSvc',
  'AppXSvc', 'ClipSVC', 'InstallService', 'LicenseManager', 'wlidsvc', 'TokenBroker', 'GamingServices',
  'GamingServicesNet',
  'wuauserv', 'UsoSvc', 'BITS', 'WaaSMedicSvc', 'DoSvc', 'TrustedInstaller',
  'WinDefend', 'WdNisSvc', 'SecurityHealthService', 'wscsvc', 'Sense', 'MDCoreSvc', 'webthreatdefsvc',
  'WpnService', 'WpnUserService',
  'defragsvc', 'StorSvc', 'VSS', 'swprv',
];
const FORBIDDEN = new Set(FORBIDDEN_SERVICES.map(s => s.toLowerCase()));

const VALUE = '{{value}}';
const GPU = '{{nvidiaGpuIndex}}';
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const UINT32_MAX = 4294967295;

const isStr = v => typeof v === 'string';
const isNonEmptyStr = v => isStr(v) && v.trim() !== '';
const isUint = (v, max) => Number.isInteger(v) && v >= 0 && v <= max;
const isUint32OrValue = v => v === VALUE || isUint(v, UINT32_MAX);
const isBool = v => typeof v === 'boolean';

// Поля каждого типа действия: [имя, проверка, обязательно].
const ACTION_SPECS = {
  reg: {
    fields: {
      hive: [v => v === 'HKLM' || v === 'HKCU', true],
      path: [v => isNonEmptyStr(v) && !v.startsWith('\\') && !/^(HK[A-Z_]+|HKEY_[A-Z_]+)\\/i.test(v), true],
      name: [isStr, true],
      valueType: [v => ['DWORD', 'QWORD', 'SZ', 'EXPAND_SZ', 'BINARY', 'MULTI_SZ'].includes(v), true],
      op: [v => v === 'set' || v === 'delete', true],
      value: [() => true, false],
    },
    check: regValueProblem,
    key: a => `reg:${a.hive}\\${a.path}\\${a.name}`.toLowerCase(),
  },
  bcd: {
    fields: {
      op: [v => v === 'set' || v === 'deletevalue', true],
      name: [v => isStr(v) && /^[a-z]+$/.test(v), true],
      value: [isStr, false],
    },
    check: a => (a.op === 'set' && !isStr(a.value) ? 'для op=set нужно строковое value'
      : a.op === 'deletevalue' && a.value !== undefined ? 'для op=deletevalue value не задаётся' : null),
    key: a => `bcd:${a.name}`.toLowerCase(),
  },
  service: {
    fields: {
      name: [isNonEmptyStr, true],
      startType: [v => ['Boot', 'System', 'Automatic', 'AutomaticDelayed', 'Manual', 'Disabled'].includes(v), true],
      stop: [isBool, false],
    },
    key: a => `svc:${a.name}`.toLowerCase(),
  },
  task: {
    fields: {
      path: [v => isStr(v) && v.startsWith('\\'), true],
      enabled: [isBool, true],
    },
    key: a => `task:${a.path}`.toLowerCase(),
  },
  power: {
    fields: {
      subgroup: [v => isStr(v) && GUID.test(v), true],
      setting: [v => isStr(v) && GUID.test(v), true],
      ac: [isUint32OrValue, true],
      dc: [isUint32OrValue, true],
      alias: [isStr, false],
    },
    key: a => `power:active/${a.subgroup}/${a.setting}`.toLowerCase(),
  },
  nic: {
    fields: {
      keyword: [isNonEmptyStr, true],
      value: [isStr, false],
      op: [v => v === 'reset', false],
    },
    check: a => (a.op === 'reset' && a.value !== undefined ? 'op=reset не принимает value'
      : a.op === undefined && a.value === undefined ? 'нужно value или op=reset' : null),
    key: a => `nic:*/${a.keyword}`.toLowerCase(),
  },
  nvdrs: {
    fields: {
      settingId: [v => isUint(v, UINT32_MAX), true],
      value: [isUint32OrValue, false],
      op: [v => v === 'restoreDefault', false],
    },
    check: a => (a.op === 'restoreDefault' && a.value !== undefined ? 'restoreDefault не принимает value'
      : a.op === undefined && a.value === undefined ? 'нужно value или op=restoreDefault' : null),
    key: a => `nvdrs:global/${a.settingId}`,
  },
  powershell: {
    fields: { script: [isNonEmptyStr, true] },
  },
};
const DYNAMIC_TYPES = ['msi', 'affinity', 'ifeo', 'powerScheme'];

function regValueProblem(a) {
  if (a.op === 'delete') return a.value === undefined || a.value === null ? null : 'для op=delete value не задаётся';
  if (a.value === undefined || a.value === null) return 'для op=set нужно value';
  const v = a.value;
  if (v === VALUE) return null;
  switch (a.valueType) {
    case 'DWORD': return isUint(v, UINT32_MAX) ? null : `DWORD вне 0…${UINT32_MAX}: ${JSON.stringify(v)}`;
    case 'QWORD': return isUint(v, Number.MAX_SAFE_INTEGER) || (isStr(v) && /^\d+$/.test(v) && BigInt(v) < 2n ** 64n)
      ? null : `неверный QWORD: ${JSON.stringify(v)}`;
    case 'SZ':
    case 'EXPAND_SZ': return isStr(v) ? null : 'строковый тип требует строку';
    case 'BINARY': return isStr(v) && /^([0-9a-f]{2})*$/i.test(v) ? null : 'BINARY — hex-строка чётной длины';
    case 'MULTI_SZ': return Array.isArray(v) && v.every(isStr) ? null : 'MULTI_SZ — массив строк';
    default: return null;
  }
}

function actionProblems(a) {
  if (!a || typeof a !== 'object' || Array.isArray(a)) return [{ code: 'E_ACTION_TYPE', message: 'действие — не объект' }];
  if (DYNAMIC_TYPES.includes(a.type)) {
    return [{ code: 'E_ACTION_TYPE', message: `динамическое действие «${a.type}» недопустимо в каталоге` }];
  }
  const spec = ACTION_SPECS[a.type];
  if (!spec) return [{ code: 'E_ACTION_TYPE', message: `неизвестный тип действия «${a.type}»` }];
  const problems = [];
  for (const [name, [ok, required]] of Object.entries(spec.fields)) {
    if (a[name] === undefined) {
      if (required) problems.push(`нет поля ${name}`);
    } else if (!ok(a[name]) && !(name === 'value' && a.type === 'reg')) {
      problems.push(`неверное поле ${name}: ${JSON.stringify(a[name])}`);
    }
  }
  for (const name of Object.keys(a)) {
    if (name !== 'type' && !(name in spec.fields)) problems.push(`лишнее поле ${name}`);
  }
  const extra = problems.length === 0 && spec.check ? spec.check(a) : null;
  if (extra) problems.push(extra);
  return problems.length ? [{ code: 'E_ACTION_FIELD', message: `${a.type}: ${problems.join('; ')}` }] : [];
}

function* strings(value, path = []) {
  if (isStr(value)) yield [value, path];
  else if (Array.isArray(value)) for (let i = 0; i < value.length; i++) yield* strings(value[i], [...path, i]);
  else if (value && typeof value === 'object') for (const [k, v] of Object.entries(value)) yield* strings(v, [...path, k]);
}

function placeholderProblems(t) {
  const out = [];
  const valueKind = t.kind === 'value' || t.kind === 'select';
  for (const section of ['apply', 'revert', 'detect']) {
    for (const [s, path] of strings(t[section] ?? null)) {
      for (const [ph] of s.matchAll(/\{\{[^}]*\}\}/g)) {
        const field = path[path.length - 1];
        const action = section === 'detect' ? t.detect : t[section][path[0]];
        if (ph === VALUE) {
          if (!valueKind) out.push(`${VALUE} в твике kind=${t.kind}`);
        } else if (ph === GPU) {
          if (!(action?.type === 'reg' && field === 'path')) out.push(`${GPU} допустим только в reg.path`);
        } else {
          out.push(`неизвестный плейсхолдер ${ph}`);
        }
      }
    }
  }
  return out;
}

function optionsProblem(t) {
  const o = t.options;
  if (t.kind === 'select') {
    if (!Array.isArray(o) || o.length < 2) return 'select требует массив options из 2+ вариантов';
    if (!o.every(x => x && isStr(x.label) && (typeof x.value === 'number' || isStr(x.value)))) return 'вариант select — {label, value}';
    if (new Set(o.map(x => typeof x.value)).size > 1) return 'у вариантов select разные типы value';
    return null;
  }
  if (t.kind === 'value') {
    if (!o || Array.isArray(o) || typeof o !== 'object') return 'value требует options {min, max, default}';
    const { min, max, default: def } = o;
    if (![min, max, def].every(n => typeof n === 'number')) return 'min, max, default должны быть числами';
    if (Object.keys(o).some(k => !['min', 'max', 'default'].includes(k))) return 'лишние поля в options';
    if (!(min <= def && def <= max)) return `нужно min ≤ default ≤ max (${min}, ${def}, ${max})`;
    return null;
  }
  return o === undefined || o === null ? null : `kind=${t.kind} не принимает options`;
}

function detectProblem(d) {
  if (d === undefined || d === null) return null;
  if (typeof d !== 'object' || Array.isArray(d)) return 'detect — объект или null';
  if (d.equals === undefined) return 'detect требует equals';
  if (d.type === 'powershell') return isNonEmptyStr(d.script) ? null : 'detect powershell требует script';
  if (d.type === 'reg') {
    const missing = ['hive', 'path', 'name'].filter(k => !isStr(d[k]));
    return missing.length ? `detect reg: нет ${missing.join(', ')}` : null;
  }
  return `detect не поддерживает type=${d.type}`;
}

function forbiddenTarget(a) {
  if (a?.type === 'service' && isStr(a.name) && FORBIDDEN.has(a.name.toLowerCase())) return a.name;
  if (a?.type === 'reg' && isStr(a.path)) {
    const m = /^SYSTEM\\(?:CurrentControlSet|ControlSet\d{3})\\Services\\([^\\]+)/i.exec(a.path);
    if (m && FORBIDDEN.has(m[1].toLowerCase())) return m[1];
  }
  return null;
}

const targetKey = a => ACTION_SPECS[a?.type]?.key?.(a) ?? null;

function powershellWarnings(t) {
  const scripts = [...(t.apply ?? []), ...(t.revert ?? [])]
    .filter(a => a?.type === 'powershell' && isStr(a.script)).map(a => a.script);
  if (scripts.length === 0) return [];
  const all = scripts.join('\n');
  const HINTS = [
    ['spi', /SystemParametersInfo/i],
    ['netsh', /\bnetsh\b/i],
    ['nic', /-NetAdapterAdvancedProperty\b/i],
    ['dns', /Set-DnsClient(ServerAddress|DohServerAddress)\b/i],
    ['iface-reg', /Tcpip\\+Parameters\\+Interfaces/i],
    ['powerScheme', /powercfg\s+\/duplicatescheme/i],
  ];
  const hints = HINTS.filter(([, re]) => re.test(all)).map(([name]) => name);
  const out = [{
    code: 'W_POWERSHELL',
    message: `${scripts.length} PowerShell-действ., отката к baseline нет` +
      (hints.length ? `; заменить нативным исполнителем: ${hints.join(', ')}` : ''),
  }];
  if (/SystemParametersInfo|\[Microsoft\.Win32\.Registry\]::CurrentUser|HKEY_CURRENT_USER/i.test(all)) {
    out.push({ code: 'W_PS_USER_CONTEXT', message: 'скрипт пишет в профиль процесса, а не интерактивного пользователя' });
  }
  if (/hateeze\\+backup/i.test(all)) {
    out.push({ code: 'W_PS_OWN_BACKUP', message: 'скрипт ведёт свой бэкап мимо журнала движка' });
  }
  return out;
}

function tweakIssues(t) {
  const out = [];
  const err = (code, message) => out.push({ level: 'error', code, message });
  if (!t || typeof t !== 'object' || Array.isArray(t)) {
    err('E_ROOT', 'элемент каталога — не объект');
    return out;
  }

  const missing = REQUIRED.filter(k => t[k] === undefined);
  if (missing.length) err('E_FIELD_MISSING', `нет полей: ${missing.join(', ')}`);
  const unknown = Object.keys(t).filter(k => !REQUIRED.includes(k) && !OPTIONAL.includes(k));
  if (unknown.length) err('E_FIELD_UNKNOWN', `неизвестные поля: ${unknown.join(', ')}`);
  for (const k of ['title', 'description']) if (t[k] !== undefined && !isNonEmptyStr(t[k])) err('E_FIELD_TYPE', `${k} — непустая строка`);
  for (const k of NULLABLE_STRINGS) if (t[k] !== undefined && t[k] !== null && !isStr(t[k])) err('E_FIELD_TYPE', `${k} — строка или null`);

  for (const [field, allowed] of Object.entries(ENUMS)) {
    if (t[field] !== undefined && !allowed.includes(t[field])) err('E_ENUM', `${field}=${JSON.stringify(t[field])}`);
  }
  if (t.os !== undefined && !(Array.isArray(t.os) && t.os.length > 0 && t.os.every(o => OS.includes(o)))) {
    err('E_ENUM', `os=${JSON.stringify(t.os)}`);
  }

  if (isStr(t.id)) {
    if (!/^[a-z][a-z0-9]*\.[a-z0-9][a-z0-9-]*$/.test(t.id)) err('E_ID', `неверный id «${t.id}»`);
    else if (isStr(t.category) && t.id.split('.')[0] !== t.category) err('E_ID_PREFIX', `префикс id не равен category «${t.category}»`);
  } else if (t.id !== undefined) {
    err('E_ID', 'id — строка');
  }

  const apply = Array.isArray(t.apply) ? t.apply : [];
  const revert = Array.isArray(t.revert) ? t.revert : [];
  for (const k of ['apply', 'revert']) if (t[k] !== undefined && !Array.isArray(t[k])) err('E_FIELD_TYPE', `${k} — массив`);
  const live = t.evidence !== 'avoid';
  if (t.kind === 'info') {
    if (apply.length || revert.length) err('E_INFO_ACTIONS', 'у kind=info apply и revert пустые');
  } else if (live && (apply.length === 0 || revert.length === 0)) {
    err('E_ACTIONS_EMPTY', 'нужны непустые apply и revert (или kind=info)');
  }

  for (const a of [...apply, ...revert]) for (const p of actionProblems(a)) err(p.code, p.message);

  for (const p of placeholderProblems(t)) err('E_PLACEHOLDER', p);
  if ((t.kind === 'value' || t.kind === 'select') && !JSON.stringify(apply).includes(VALUE)) {
    err('E_PLACEHOLDER_MISSING', `kind=${t.kind} без ${VALUE} в apply`);
  }

  const optProblem = ENUMS.kind.includes(t.kind) ? optionsProblem(t) : null;
  if (optProblem) err('E_OPTIONS', optProblem);
  const detProblem = detectProblem(t.detect);
  if (detProblem) err('E_DETECT', detProblem);

  if (live && !(Array.isArray(t.sources) && t.sources.some(s => isStr(s) && /^https?:\/\//.test(s)))) {
    err('E_SOURCES', 'нужен хотя бы один URL источника');
  }

  if (live) {
    const hits = [...new Set([...apply, ...revert].map(forbiddenTarget).filter(Boolean))];
    if (hits.length) err('E_FORBIDDEN', `запрещённые службы: ${hits.join(', ')}`);

    const revertKeys = new Set(revert.map(targetKey).filter(Boolean));
    const uncovered = [...new Set(apply.map(targetKey).filter(k => k && !revertKeys.has(k)))];
    if (uncovered.length && revert.length) err('E_REVERT_COVERAGE', `revert не покрывает: ${uncovered.join(', ')}`);

    for (const w of powershellWarnings(t)) out.push({ level: 'warning', ...w });
  }
  return out;
}

/**
 * Проверяет набор файлов каталога.
 * @param {{name: string, text: string}[]} files
 * @returns {{level: 'error'|'warning', code: string, file: string, id?: string, message: string}[]}
 */
export function validateCatalogs(files) {
  const issues = [];
  const seen = new Map();
  for (const { name, text } of files) {
    let data;
    try {
      data = JSON.parse(text.replace(/^﻿/, ''));
    } catch (e) {
      issues.push({ level: 'error', code: 'E_JSON', file: name, message: e.message });
      continue;
    }
    if (!Array.isArray(data)) {
      issues.push({ level: 'error', code: 'E_ROOT', file: name, message: 'корень файла — не массив' });
      continue;
    }
    for (const t of data) {
      const id = isStr(t?.id) ? t.id : undefined;
      for (const i of tweakIssues(t)) issues.push({ ...i, file: name, id });
      if (id) {
        if (seen.has(id)) issues.push({ level: 'error', code: 'E_ID_DUP', file: name, id, message: `id уже есть в ${seen.get(id)}` });
        else seen.set(id, name);
      }
    }
  }
  return issues;
}
