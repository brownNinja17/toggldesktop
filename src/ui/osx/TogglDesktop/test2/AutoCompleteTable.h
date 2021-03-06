//
//  AutoCompleteTable.h
//  LiteComplete
//
//  Created by Indrek Vändrik on 21/02/2018.
//  Copyright © 2018 Toggl. All rights reserved.
//

#import <Cocoa/Cocoa.h>
#import "AutoCompleteTableCell.h"

@interface AutoCompleteTable : NSTableView
@property NSInteger lastSelected;
@property NSInteger lastSavedSelected;
- (void)nextItem;
- (void)previousItem;
- (AutoCompleteTableCell *)getSelectedCell:(NSInteger)row;
- (void)setFirstRowAsSelected;
- (void)setCurrentSelected:(NSInteger)index next:(BOOL)isNext;
- (void)resetSelected;
@end
