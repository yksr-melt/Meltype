// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 lnkiai

#include "DisplayAttributes.h"

namespace meltype {
namespace {

struct AttributeSpec {
    const GUID* guid;
    const wchar_t* description;
    TF_DA_LINESTYLE line;
    BOOL bold;
    TF_DA_ATTR_INFO attr;
};

// 色はアプリの文字の色のまま (TF_CT_NONE)。下線の形だけで区別する
const AttributeSpec kSpecs[] = {
    {&GUID_AttrInput, L"Meltype: 入力中", TF_LS_DOT, FALSE, TF_ATTR_INPUT},
    {&GUID_AttrConverted, L"Meltype: 変換した文節", TF_LS_SOLID, FALSE, TF_ATTR_CONVERTED},
    {&GUID_AttrTarget, L"Meltype: 選んでいる文節", TF_LS_SOLID, TRUE, TF_ATTR_TARGET_CONVERTED},
};

class DisplayAttributeInfo final : public ITfDisplayAttributeInfo {
public:
    explicit DisplayAttributeInfo(const AttributeSpec& spec) : spec_(spec) {}

    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override {
        if (ppv == nullptr) return E_INVALIDARG;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_ITfDisplayAttributeInfo)) {
            *ppv = static_cast<ITfDisplayAttributeInfo*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&refs_); }
    STDMETHODIMP_(ULONG) Release() override {
        ULONG count = InterlockedDecrement(&refs_);
        if (count == 0) delete this;
        return count;
    }

    STDMETHODIMP GetGUID(GUID* guid) override {
        if (guid == nullptr) return E_INVALIDARG;
        *guid = *spec_.guid;
        return S_OK;
    }
    STDMETHODIMP GetDescription(BSTR* description) override {
        if (description == nullptr) return E_INVALIDARG;
        *description = SysAllocString(spec_.description);
        return *description ? S_OK : E_OUTOFMEMORY;
    }
    STDMETHODIMP GetAttributeInfo(TF_DISPLAYATTRIBUTE* attribute) override {
        if (attribute == nullptr) return E_INVALIDARG;
        *attribute = {};
        attribute->crText.type = TF_CT_NONE;
        attribute->crBk.type = TF_CT_NONE;
        attribute->lsStyle = spec_.line;
        attribute->fBoldLine = spec_.bold;
        attribute->crLine.type = TF_CT_NONE;
        attribute->bAttr = spec_.attr;
        return S_OK;
    }
    STDMETHODIMP SetAttributeInfo(const TF_DISPLAYATTRIBUTE*) override { return E_NOTIMPL; }
    STDMETHODIMP Reset() override { return S_OK; }

private:
    ~DisplayAttributeInfo() = default;
    LONG refs_ = 1;
    const AttributeSpec& spec_;
};

class DisplayAttributeEnum final : public IEnumTfDisplayAttributeInfo {
public:
    explicit DisplayAttributeEnum(ULONG index = 0) : index_(index) {}

    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override {
        if (ppv == nullptr) return E_INVALIDARG;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IEnumTfDisplayAttributeInfo)) {
            *ppv = static_cast<IEnumTfDisplayAttributeInfo*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&refs_); }
    STDMETHODIMP_(ULONG) Release() override {
        ULONG count = InterlockedDecrement(&refs_);
        if (count == 0) delete this;
        return count;
    }

    STDMETHODIMP Clone(IEnumTfDisplayAttributeInfo** ppEnum) override {
        if (ppEnum == nullptr) return E_INVALIDARG;
        *ppEnum = new DisplayAttributeEnum(index_);
        return S_OK;
    }
    STDMETHODIMP Next(ULONG count, ITfDisplayAttributeInfo** infos, ULONG* fetched) override {
        if (infos == nullptr) return E_INVALIDARG;
        ULONG n = 0;
        while (n < count && index_ < ARRAYSIZE(kSpecs)) infos[n++] = new DisplayAttributeInfo(kSpecs[index_++]);
        if (fetched != nullptr) *fetched = n;
        return n == count ? S_OK : S_FALSE;
    }
    STDMETHODIMP Reset() override {
        index_ = 0;
        return S_OK;
    }
    STDMETHODIMP Skip(ULONG count) override {
        index_ = (index_ + count > ARRAYSIZE(kSpecs)) ? ARRAYSIZE(kSpecs) : index_ + count;
        return index_ < ARRAYSIZE(kSpecs) ? S_OK : S_FALSE;
    }

private:
    ~DisplayAttributeEnum() = default;
    LONG refs_ = 1;
    ULONG index_;
};

}  // namespace

ITfDisplayAttributeInfo* CreateDisplayAttributeInfo(REFGUID guid) {
    for (const AttributeSpec& spec : kSpecs) {
        if (IsEqualGUID(guid, *spec.guid)) return new DisplayAttributeInfo(spec);
    }
    return nullptr;
}

IEnumTfDisplayAttributeInfo* CreateDisplayAttributeEnum() { return new DisplayAttributeEnum(); }

}  // namespace meltype
