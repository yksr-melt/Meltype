# SPDX-License-Identifier: GPL-3.0-or-later
"""Additional provider boundaries: persistent/imported/custom and engine replies."""
import json
from pathlib import Path
from check import cases,keys

def generate():
    rows=[];base=cases()[0]
    def add(name,raw,files=None,settings=None,tail=None,converter=None,before='',after=''):
        row=json.loads(json.dumps(base));row.update(name=name,keys=keys(raw)+(tail if tail is not None else [[32,32,0],[40,0,0],[13,0,0]]),before=before,after=after)
        row['settings'].update(settings or {})
        if files:row['files']=files
        if converter is not None:row['converter']=converter
        rows.append(row)
    for engine in ['System','Mozc','Hybrid']:
        add('engine-'+engine,'hashi',settings={'ConversionEngine':engine})
        add('empty-engine-'+engine,'hashi',settings={'ConversionEngine':engine},converter={})
    add('brand','shaomi')
    add('phrase','shosen')
    add('phrase-and-particle','shosenha')
    add('registered-and-particle','merutoha',files={'userdict.txt':'めると\tMeltype\n'})
    plist='<?xml version="1.0"?><plist version="1.0"><array><dict><key>shortcut</key><string>めると</string><key>phrase</key><string>Imported</string></dict></array></plist>'
    add('plist','meruto',files={'dictionaries/words.plist':plist})
    add('plist-priority','meruto',files={'dictionaries/words.plist':plist,'userdict.txt':'めると\tLocal\n'})
    add('invalid-plist-skipped','meruto',files={'dictionaries/broken.plist':'not xml'})
    add('custom-misspelling','gasuterayaki',files={'dictionaries/misspellings.txt':'ガステラヤキ\tカステラヤキ\n'},settings={'DetectionLevel':'Manual'},tail=[[9,0,0],[13,0,0]])
    add('custom-misspelling-override','shumire-shon',files={'dictionaries/misspellings.txt':'シュミレーション\tシミュレーター\n'},settings={'DetectionLevel':'Manual'},tail=[[9,0,0],[13,0,0]])
    add('translation-reordered','ki',files={'translations.json':json.dumps({'き':{'wood':3,'tree':1}})})
    add('predicted-user-word','meru',files={'userdict.txt':'めるとのみらい\tMeltypeの未来\n'},settings={'PredictiveCandidates':True},tail=[[9,0,0],[13,0,0]])
    add('predicted-selected-word','hash',files={'conversions.json':json.dumps({'はしもと':{'Text':'橋本','Used':'2026-10-20T00:00:00Z'}})},settings={'DetectionLevel':'Manual','PredictiveCandidates':True},tail=[[9,0,0],[13,0,0]])
    add('typed-clock','3ji15fun')
    add('engine-emoji-primary','egao',converter={'clauses':{'えがお':[['えがお','😊']]},'candidates':{'えがお':['😊','笑顔','😀']}})
    add('number-width','123',converter={'clauses':{'123':[['123','１２３']]},'candidates':{'123':['１２３','一二三']}})
    add('punctuation-width','#',settings={'SigilWordsDirect':False},converter={'clauses':{'＃':[['＃','＃']]}})
    add('separate-key-width','hashi',tail=[[29,0,0],[32,32,0],[40,0,0],[13,0,0]],settings={'SeparateKeyRoles':True})
    add('multiple-clauses','hashikami',converter={'clauses':{'はしかみ':[['はし','橋'],['かみ','紙']]},'candidates':{'はし':['橋','箸'],'かみ':['紙','神']}})
    add('context-other-clause','atsuihashi',files={'dictionaries/contexts.txt':'あつい 厚い : 橋\n'},converter={'clauses':{'あついはし':[['あつい','暑い'],['はし','橋']]}})
    add('both-input-formal-composition','konnichiwa',settings={'InputStyle':'Both'},tail=[[13,0,0]])
    return rows



def dictionary_cases():
    rows=[]
    for name, filename, text in [('custom-proper','propernouns.txt','MeruTe'),('custom-english','english.txt','merute'),('custom-japanese','japanese.txt','merute')]:
        row=cases()[0];row.update(name=name,keys=keys('merute')+[[32,32,0],[40,0,0],[13,0,0]],files={'dictionaries/'+filename:text+'\n'});rows.append(row)
    return rows


if __name__=='__main__':
    import argparse
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('output',type=Path);parser.add_argument('--dictionaries',action='store_true');args=parser.parse_args()
    args.output.write_text(json.dumps(dictionary_cases() if args.dictionaries else generate(),ensure_ascii=False,indent=2),encoding='utf-8')
