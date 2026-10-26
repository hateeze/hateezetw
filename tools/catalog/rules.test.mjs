import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { validateCatalogs } from './rules.mjs';

const REG_A = { type: 'reg', hive: 'HKLM', path: 'SOFTWARE\\Test', name: 'A', valueType: 'DWORD', op: 'set', value: 1 };
const REG_A_DEFAULT = { ...REG_A, value: 0 };

function tweak(overrides = {}) {
  return {
    id: 'power.sample',
    category: 'power',
    title: 'Пример',
    description: 'Описание',
    recommendation: null,
    evidence: 'situational',
    evidenceNote: null,
    risk: 'low',
    riskNote: null,
    reboot: 'none',
    os: ['win10', 'win11'],
    osNote: null,
    vendor: 'any',
    kind: 'toggle',
    apply: [REG_A],
    revert: [REG_A_DEFAULT],
    detect: null,
    sources: ['https://learn.microsoft.com/'],
    ...overrides,
  };
}

const file = (name, data) => ({ name, text: typeof data === 'string' ? data : JSON.stringify(data) });
const run = (...tweaks) => validateCatalogs([file('a.tweaks.json', tweaks)]);
const codes = (issues, level) => issues.filter(i => !level || i.level === level).map(i => i.code);
const errors = issues => codes(issues, 'error');
const warnings = issues => codes(issues, 'warning');

test('valid toggle has no issues', () => {
  assert.deepEqual(run(tweak()), []);
});

test('invalid JSON is reported', () => {
  assert.deepEqual(errors(validateCatalogs([file('bad.json', '[{')])), ['E_JSON']);
});

test('root must be an array', () => {
  assert.deepEqual(errors(validateCatalogs([file('obj.json', { tweaks: [] })])), ['E_ROOT']);
});

test('missing required field', () => {
  const t = tweak();
  delete t.risk;
  assert.ok(errors(run(t)).includes('E_FIELD_MISSING'));
});

test('unknown field is an error', () => {
  assert.deepEqual(errors(run(tweak({ range: { min: 1 } }))), ['E_FIELD_UNKNOWN']);
});

test('enum values are checked', () => {
  assert.deepEqual(errors(run(tweak({ evidence: 'maybe' }))), ['E_ENUM']);
  assert.deepEqual(errors(run(tweak({ reboot: 'later' }))), ['E_ENUM']);
  assert.deepEqual(errors(run(tweak({ os: ['win7'] }))), ['E_ENUM']);
  assert.deepEqual(errors(run(tweak({ os: [] }))), ['E_ENUM']);
});

test('category must be known and match id prefix', () => {
  assert.ok(errors(run(tweak({ id: 'misc.sample', category: 'misc' }))).includes('E_ENUM'));
  assert.deepEqual(errors(run(tweak({ id: 'cpu.sample' }))), ['E_ID_PREFIX']);
});

test('id format', () => {
  assert.deepEqual(errors(run(tweak({ id: 'power.Bad_Id' }))), ['E_ID']);
});

test('duplicate ids across files', () => {
  const issues = validateCatalogs([file('a.json', [tweak()]), file('b.json', [tweak()])]);
  assert.deepEqual(errors(issues), ['E_ID_DUP']);
  assert.equal(issues[0].file, 'b.json');
});

test('non-avoid toggle needs apply and revert', () => {
  assert.deepEqual(errors(run(tweak({ revert: [] }))), ['E_ACTIONS_EMPTY']);
  assert.deepEqual(errors(run(tweak({ apply: [] }))), ['E_ACTIONS_EMPTY']);
});

test('avoid may have empty actions', () => {
  assert.deepEqual(run(tweak({ evidence: 'avoid', apply: [], revert: [] })), []);
});

test('info kind must have no actions', () => {
  assert.deepEqual(run(tweak({ kind: 'info', apply: [], revert: [] })), []);
  assert.deepEqual(errors(run(tweak({ kind: 'info' }))), ['E_INFO_ACTIONS']);
});

test('unknown and dynamic action types are rejected', () => {
  const bad = { type: 'wmi', query: 'x' };
  assert.deepEqual(errors(run(tweak({ apply: [bad], revert: [bad] }))), ['E_ACTION_TYPE', 'E_ACTION_TYPE']);
  const msi = { type: 'msi', target: 'PCI\\X' };
  assert.ok(errors(run(tweak({ apply: [msi], revert: [REG_A_DEFAULT] }))).includes('E_ACTION_TYPE'));
});

test('reg action fields', () => {
  const withApply = a => run(tweak({ apply: [a], revert: [{ ...a, op: 'delete', value: null }] }));
  assert.deepEqual(withApply({ ...REG_A, value: 4294967295 }), []);
  assert.deepEqual(errors(withApply({ ...REG_A, value: 4294967296 })), ['E_ACTION_FIELD']);
  assert.deepEqual(errors(withApply({ ...REG_A, value: -1 })), ['E_ACTION_FIELD']);
  assert.deepEqual(errors(withApply({ ...REG_A, hive: 'HKCR' })), ['E_ACTION_FIELD', 'E_ACTION_FIELD']);
  assert.deepEqual(errors(withApply({ ...REG_A, valueType: 'BINARY', value: 'ABC' })), ['E_ACTION_FIELD']);
  assert.deepEqual(withApply({ ...REG_A, valueType: 'BINARY', value: '0A0B' }), []);
  assert.deepEqual(withApply({ ...REG_A, valueType: 'QWORD', value: '18446744073709551615' }), []);
  assert.deepEqual(withApply({ ...REG_A, valueType: 'MULTI_SZ', value: ['a', 'b'] }), []);
  assert.deepEqual(errors(withApply({ ...REG_A, valueType: 'SZ', value: 5 })), ['E_ACTION_FIELD']);
  const noValue = { ...REG_A };
  delete noValue.value;
  assert.deepEqual(errors(withApply(noValue)), ['E_ACTION_FIELD']);
  assert.deepEqual(errors(withApply({ ...REG_A, extra: true })), ['E_ACTION_FIELD', 'E_ACTION_FIELD']);
  assert.deepEqual(errors(withApply({ ...REG_A, path: 'HKLM\\SOFTWARE\\Test' })), ['E_ACTION_FIELD', 'E_ACTION_FIELD']);
});

test('reg delete must not carry a value', () => {
  const del = { ...REG_A, op: 'delete', value: 0 };
  assert.deepEqual(errors(run(tweak({ revert: [del] }))), ['E_ACTION_FIELD']);
});

test('bcd action fields', () => {
  const set = { type: 'bcd', op: 'set', name: 'useplatformclock', value: 'yes' };
  const del = { type: 'bcd', op: 'deletevalue', name: 'useplatformclock' };
  assert.deepEqual(run(tweak({ apply: [set], revert: [del] })), []);
  const setNoValue = { type: 'bcd', op: 'set', name: 'useplatformclock' };
  assert.deepEqual(errors(run(tweak({ apply: [setNoValue], revert: [del] }))), ['E_ACTION_FIELD']);
});

test('service action fields', () => {
  const svc = { type: 'service', name: 'SysMain', startType: 'Disabled', stop: true };
  const back = { type: 'service', name: 'SysMain', startType: 'Automatic', stop: false };
  assert.deepEqual(run(tweak({ id: 'services.x', category: 'services', apply: [svc], revert: [back] })), []);
  const bad = { ...svc, startType: 'Off' };
  assert.deepEqual(errors(run(tweak({ id: 'services.x', category: 'services', apply: [bad], revert: [back] }))), ['E_ACTION_FIELD']);
});

test('task path must start with backslash', () => {
  const t = { type: 'task', path: 'Microsoft\\Windows\\X', enabled: false };
  assert.deepEqual(errors(run(tweak({ id: 'tasks.x', category: 'tasks', apply: [t], revert: [{ ...t, enabled: true }] }))), ['E_ACTION_FIELD', 'E_ACTION_FIELD']);
});

test('power action needs GUIDs and uint values', () => {
  const p = { type: 'power', subgroup: '2a737441-1930-4402-8d77-b2bebba308a3', setting: '48e6b7a6-50f5-4782-a5d4-53bb8f07e226', alias: 'USB', ac: 0, dc: 1 };
  assert.deepEqual(run(tweak({ apply: [p], revert: [{ ...p, ac: 1 }] })), []);
  const bad = { ...p, setting: 'USBSUSPEND' };
  assert.deepEqual(errors(run(tweak({ apply: [bad], revert: [{ ...bad, ac: 1 }] }))), ['E_ACTION_FIELD', 'E_ACTION_FIELD']);
});

test('nvdrs set needs value, restoreDefault must not have one', () => {
  const set = { type: 'nvdrs', settingId: 274197361, value: 1 };
  const restore = { type: 'nvdrs', settingId: 274197361, op: 'restoreDefault' };
  const nv = o => tweak({ id: 'nvidia.x', category: 'nvidia', vendor: 'nvidia', ...o });
  assert.deepEqual(run(nv({ apply: [set], revert: [restore] })), []);
  assert.deepEqual(errors(run(nv({ apply: [set], revert: [{ ...restore, value: 5 }] }))), ['E_ACTION_FIELD']);
  assert.deepEqual(errors(run(nv({ apply: [{ type: 'nvdrs', settingId: 274197361 }], revert: [restore] }))), ['E_ACTION_FIELD']);
});

test('nic value must be a string', () => {
  const n = { type: 'nic', keyword: '*EEE', value: 0 };
  assert.deepEqual(errors(run(tweak({ id: 'network.x', category: 'network', apply: [n], revert: [{ ...n, value: '1' }] }))), ['E_ACTION_FIELD']);
});

test('nic reset restores the driver default without a value', () => {
  const set = { type: 'nic', keyword: '*EEE', value: '0' };
  const reset = { type: 'nic', keyword: '*EEE', op: 'reset' };
  const net = o => tweak({ id: 'network.x', category: 'network', apply: [set], ...o });
  assert.deepEqual(run(net({ revert: [reset] })), []);
  assert.deepEqual(errors(run(net({ revert: [{ ...reset, value: '1' }] }))), ['E_ACTION_FIELD']);
  assert.deepEqual(errors(run(net({ apply: [{ ...reset }], revert: [reset] }))), []);
  assert.deepEqual(errors(run(net({ revert: [{ type: 'nic', keyword: '*EEE' }] }))), ['E_ACTION_FIELD']);
});

test('powershell hints name the native replacement', () => {
  const hint = script => run(tweak({ id: 'network.x', category: 'network',
    apply: [{ type: 'powershell', script }], revert: [{ type: 'powershell', script }] }))[0].message;
  const dns = hint('foreach ($a in @(Get-NetAdapter -Physical)) { Set-DnsClientServerAddress -InterfaceIndex $a.ifIndex }');
  assert.match(dns, /dns/);
  assert.doesNotMatch(dns, /nic/);
  assert.match(hint('Get-NetAdapterAdvancedProperty -Name x | Reset-NetAdapterAdvancedProperty'), /nic/);
  assert.match(hint("$b='HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces'"), /iface-reg/);
  assert.match(hint('powercfg /duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61 x'), /powerScheme/);
});

test('powershell script must be non-empty', () => {
  const ps = { type: 'powershell', script: '' };
  assert.ok(errors(run(tweak({ apply: [ps], revert: [ps] }))).includes('E_ACTION_FIELD'));
});

test('{{value}} only in value and select tweaks', () => {
  const a = { ...REG_A, value: '{{value}}' };
  assert.deepEqual(errors(run(tweak({ apply: [a] }))), ['E_PLACEHOLDER']);
});

test('unknown placeholders are rejected', () => {
  const a = { ...REG_A, path: 'SOFTWARE\\{{gpu}}' };
  assert.ok(errors(run(tweak({ apply: [a], revert: [{ ...a, value: 0 }] }))).includes('E_PLACEHOLDER'));
});

test('{{nvidiaGpuIndex}} is allowed only in reg path', () => {
  const ok = { ...REG_A, path: 'SYSTEM\\Class\\{{nvidiaGpuIndex}}' };
  assert.deepEqual(run(tweak({ apply: [ok], revert: [{ ...ok, op: 'delete', value: null }] })), []);
  const bad = { ...REG_A, name: '{{nvidiaGpuIndex}}' };
  assert.ok(errors(run(tweak({ apply: [bad], revert: [{ ...bad, value: 0 }] }))).includes('E_PLACEHOLDER'));
});

test('value and select tweaks must use {{value}} in apply', () => {
  const t = tweak({ kind: 'value', options: { min: 0, max: 10, default: 5 } });
  assert.deepEqual(errors(run(t)), ['E_PLACEHOLDER_MISSING']);
  const ok = tweak({ kind: 'value', options: { min: 0, max: 10, default: 5 }, apply: [{ ...REG_A, value: '{{value}}' }] });
  assert.deepEqual(run(ok), []);
});

test('options shape depends on kind', () => {
  const ph = { apply: [{ ...REG_A, value: '{{value}}' }] };
  const select = opts => run(tweak({ kind: 'select', options: opts, ...ph }));
  assert.deepEqual(select([{ label: 'a', value: 1 }, { label: 'b', value: 2 }]), []);
  assert.deepEqual(errors(select([{ label: 'a', value: 1 }])), ['E_OPTIONS']);
  assert.deepEqual(errors(select([{ label: 'a', value: 1 }, { label: 'b', value: 'x' }])), ['E_OPTIONS']);
  assert.deepEqual(errors(select({ min: 0, max: 1, default: 0 })), ['E_OPTIONS']);
  assert.deepEqual(errors(run(tweak({ kind: 'select', ...ph }))), ['E_OPTIONS']);
  const value = opts => run(tweak({ kind: 'value', options: opts, ...ph }));
  assert.deepEqual(errors(value({ min: 10, max: 1, default: 5 })), ['E_OPTIONS']);
  assert.deepEqual(errors(value([{ label: 'cs2.exe', value: 'cs2.exe' }])), ['E_OPTIONS']);
  assert.deepEqual(errors(run(tweak({ options: [{ label: 'a', value: 1 }, { label: 'b', value: 2 }] }))), ['E_OPTIONS']);
});

test('detect shapes', () => {
  assert.deepEqual(run(tweak({ detect: { type: 'powershell', script: 'Write-Output True', equals: 'True' } })), []);
  assert.deepEqual(run(tweak({ detect: { type: 'reg', hive: 'HKCU', path: 'Control Panel\\Mouse', name: 'MouseSpeed', valueType: 'SZ', equals: '0' } })), []);
  assert.deepEqual(errors(run(tweak({ detect: { type: 'powershell', script: 'x' } }))), ['E_DETECT']);
  assert.deepEqual(errors(run(tweak({ detect: { type: 'service', name: 'x', equals: 'x' } }))), ['E_DETECT']);
});

test('non-avoid tweaks need a source URL', () => {
  assert.deepEqual(errors(run(tweak({ sources: [] }))), ['E_SOURCES']);
  assert.deepEqual(errors(run(tweak({ sources: ['valleyofdoom'] }))), ['E_SOURCES']);
  assert.deepEqual(run(tweak({ evidence: 'avoid', sources: [] })), []);
});

test('forbidden services are rejected in non-avoid tweaks', () => {
  const svc = name => ({ type: 'service', name, startType: 'Disabled', stop: true });
  const svcBack = name => ({ type: 'service', name, startType: 'Manual', stop: false });
  const t = (name, extra = {}) => tweak({ id: 'services.x', category: 'services', apply: [svc(name)], revert: [svcBack(name)], ...extra });
  assert.deepEqual(errors(run(t('vgc'))), ['E_FORBIDDEN']);
  assert.deepEqual(errors(run(t('VGK'))), ['E_FORBIDDEN']);
  assert.deepEqual(run(t('vgc', { evidence: 'avoid' })), []);
  const reg = { ...REG_A, path: 'SYSTEM\\CurrentControlSet\\Services\\BEService', name: 'Start', value: 4 };
  assert.deepEqual(errors(run(tweak({ apply: [reg], revert: [{ ...reg, value: 3 }] }))), ['E_FORBIDDEN']);
});

test('revert must cover every apply target', () => {
  const other = { ...REG_A_DEFAULT, name: 'B' };
  assert.deepEqual(errors(run(tweak({ revert: [other] }))), ['E_REVERT_COVERAGE']);
});

test('powershell in a live tweak gives warnings with hints', () => {
  const spi = { type: 'powershell', script: 'Add-Type ... SystemParametersInfo ...' };
  const issues = run(tweak({ id: 'input.x', category: 'input', apply: [spi], revert: [spi] }));
  assert.deepEqual(errors(issues), []);
  assert.deepEqual(warnings(issues), ['W_POWERSHELL', 'W_PS_USER_CONTEXT']);
  assert.match(issues[0].message, /spi/);
});

test('powershell own backup is flagged', () => {
  const ps = { type: 'powershell', script: "$bk=Join-Path $env:ProgramData 'hateeze\\backup'; netsh int tcp show global" };
  const issues = run(tweak({ id: 'network.x', category: 'network', apply: [ps], revert: [ps] }));
  assert.deepEqual(warnings(issues), ['W_POWERSHELL', 'W_PS_OWN_BACKUP']);
  assert.match(issues[0].message, /netsh/);
});

test('avoid tweaks get no powershell warnings', () => {
  const ps = { type: 'powershell', script: 'SystemParametersInfo' };
  assert.deepEqual(run(tweak({ evidence: 'avoid', apply: [ps], revert: [ps] })), []);
});

test('CLI exits with 1 on errors and 0 when only warnings', () => {
  const dir = mkdtempSync(join(tmpdir(), 'hz-catalog-'));
  const cli = join(dirname(fileURLToPath(import.meta.url)), 'validate.mjs');
  const good = join(dir, 'good.tweaks.json');
  writeFileSync(good, JSON.stringify([tweak()]));
  execFileSync(process.execPath, [cli, good], { stdio: 'pipe' });
  const bad = join(dir, 'bad.tweaks.json');
  writeFileSync(bad, JSON.stringify([tweak({ risk: 'huge' })]));
  assert.throws(() => execFileSync(process.execPath, [cli, bad], { stdio: 'pipe' }), e => e.status === 1);
});
